using Dapper;
using MySqlConnector;
using System.Data;
using Plus.Database;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class CredentialUtcDatabaseTests
{
    [RoomComponentDatabaseFact]
    public async Task MigrationConvertsTheRealUnsignedIntegerLegacyShape()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_refactor_tests_auth_" + Guid.NewGuid().ToString("N");
        var builder = new MySqlConnectionStringBuilder(root) { Database = "", Pooling = false };
        await using var server = new MySqlConnection(builder.ConnectionString);
        await server.OpenAsync();
        await server.ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET latin1");

        try {
            builder.Database = schema;
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(LegacySchema.Replace("DECIMAL(20,6)", "INT UNSIGNED", StringComparison.Ordinal));
            await connection.ExecuteAsync("INSERT INTO users VALUES (1,'',2208988860,0,NULL,0); " +
                "INSERT INTO user_sessions VALUES ('s',1,2208988800,NULL); " +
                "INSERT INTO user_access_tokens VALUES (1,1,'s',REPEAT('a',64),2208988800,2208988860,NULL); " +
                "INSERT INTO user_remember_tokens VALUES (1,1,'s',REPEAT('b',64),2208988800,2208988860,NULL,0,NULL)");

            await connection.ExecuteAsync(File.ReadAllText(Repo("Database/Migrations/40_UseUtcCredentialTimes.sql")));

            Assert.Equal(new DateTime(2040, 1, 1, 0, 1, 0),
                await connection.QuerySingleAsync<DateTime>("SELECT auth_ticket_expires_at FROM users WHERE id=1"));
            Assert.Equal(new DateTime(2040, 1, 1),
                await connection.QuerySingleAsync<DateTime>("SELECT created_at FROM user_sessions WHERE id='s'"));
        }
        finally {
            await server.ExecuteAsync($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    [RoomComponentDatabaseFact]
    public async Task MigrationRejectsInvalidTombstonesBeforeAlterAndPreservesNativeInstants()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;

        foreach (var sqlMode in new[] { "STRICT_ALL_TABLES", "" }) {
            foreach (var tombstone in new[] { "access.revoked", "remember.used", "remember.revoked", "session.revoked" }) {
                foreach (var invalid in new[] { 0m, -1m, 253402300800m }) {
                    await VerifyFailedPreflight(root, sqlMode, tombstone, invalid);
                }
            }

            await VerifySuccessfulDecimalMigration(root, sqlMode);
        }
    }

    [RoomComponentDatabaseFact]
    public async Task SessionIssuanceUsesOneNormalizedInstantAcrossEveryCredentialWrite()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        await WithSchema(root, async (builder, connection) =>
        {
            await SeedDecimalLegacy(connection, "STRICT_ALL_TABLES");
            await connection.ExecuteAsync(Migration);
            await connection.ExecuteAsync(
                "DELETE FROM user_access_tokens; DELETE FROM user_remember_tokens; DELETE FROM user_sessions; " +
                "UPDATE users SET auth_ticket='', auth_ticket_expires_at=NULL, auth_ticket_session=NULL, auth_ticket_exchanged=0 WHERE id=1; " +
                "ALTER TABLE users ADD username VARCHAR(64) NULL, ADD password VARCHAR(255) NULL, ADD ip_last VARCHAR(45) NULL; " +
                "UPDATE users SET username='clock-user', password='', ip_last='' WHERE id=1; " +
                "CREATE TABLE bans (bantype VARCHAR(16) NOT NULL, value VARCHAR(64) NOT NULL, reason VARCHAR(255) NOT NULL, expire DATETIME(6) NULL)");

            SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
            var clock = new AdvancingTime(new DateTimeOffset(2041, 2, 3, 9, 5, 6, TimeSpan.FromHours(5)), TimeSpan.FromMinutes(1));
            var database = new TestDatabase(builder.ConnectionString);
            var options = AuthTestConfig.Options(c =>
            {
                c.AccessTokenLifetimeMinutes = 10;
                c.RememberTokenLifetimeDays = 30;
                c.RememberReuseGraceSeconds = 30;
                c.SsoTicketLifetimeSeconds = 60;
            });
            var tickets = new SsoTicketStore(database, clock, options);
            var access = new AccessTokenStore(database, clock, options);
            var remember = new RememberTokenStore(database, clock, options);
            var generations = new CredentialGenerations(database, clock);
            var accounts = new AccountStore(database, clock, options);
            var issuer = new SessionIssuer(tickets, access, remember, generations, accounts, new BanLookup(database, clock), clock);

            clock.ResetReads();
            var issued = (await issuer.Issue(1, "clock-user", 0, "203.0.113.1", remember: true))!;
            Assert.Equal(1, clock.Reads);
            await AssertSessionTimes(connection, issued, clock.LastUtc, withTicket: true, assertSessionCreated: true);

            clock.ResetReads();
            var resumedWithTicket = await issuer.Resume(issued.RememberToken!.Value.Value, "203.0.113.2", withTicket: true);
            Assert.Equal(ResumeStatus.Resumed, resumedWithTicket.Status);
            Assert.Equal(1, clock.Reads);
            await AssertSessionTimes(connection, resumedWithTicket.Session!, clock.LastUtc, withTicket: true);

            clock.ResetReads();
            var resumedWithoutTicket = await issuer.Resume(resumedWithTicket.Session!.RememberToken!.Value.Value, "203.0.113.3", withTicket: false);
            Assert.Equal(ResumeStatus.Resumed, resumedWithoutTicket.Status);
            Assert.Equal(1, clock.Reads);
            Assert.Equal(default, resumedWithoutTicket.Session!.SsoTicket);
            await AssertSessionTimes(connection, resumedWithoutTicket.Session, clock.LastUtc, withTicket: false);

            clock.ResetReads();
            var exchangeSource = (await issuer.Issue(1, "clock-user", 0, "203.0.113.4"))!;
            Assert.Equal(1, clock.Reads);
            clock.ResetReads();
            var exchanged = await issuer.ExchangeTicket(exchangeSource.SsoTicket.Value);
            Assert.NotNull(exchanged);
            Assert.Equal(1, clock.Reads);
            await AssertTokenTimes(connection, "user_access_tokens", exchanged!.Value.Value, clock.LastUtc, TimeSpan.FromMinutes(10));

            clock.ResetReads();
            await access.Issue(1);
            Assert.Equal(1, clock.Reads);
            clock.ResetReads();
            await tickets.Issue(1);
            Assert.Equal(1, clock.Reads);
            clock.ResetReads();
            var standaloneRemember = await remember.Issue(1);
            Assert.Equal(1, clock.Reads);
            clock.ResetReads();
            var rotation = await remember.Rotate(standaloneRemember.Value);
            Assert.Equal(1, clock.Reads);
            clock.ResetReads();
            await remember.Continue(1, rotation.FamilyId);
            Assert.Equal(1, clock.Reads);

            var before = await connection.QuerySingleAsync<MutationSnapshot>(
                "SELECT (SELECT COUNT(*) FROM user_sessions) AS Sessions, (SELECT COUNT(*) FROM user_access_tokens) AS Access, " +
                "(SELECT COUNT(*) FROM user_remember_tokens) AS Remember, ip_last AS Address FROM users WHERE id=1");
            var nearMaximum = new AdvancingTime(DateTimeOffset.MaxValue.AddSeconds(-30), TimeSpan.FromSeconds(1));
            var nearMaximumIssuer = new SessionIssuer(
                new SsoTicketStore(database, nearMaximum, options),
                new AccessTokenStore(database, nearMaximum, options),
                new RememberTokenStore(database, nearMaximum, options),
                new CredentialGenerations(database, nearMaximum),
                new AccountStore(database, nearMaximum, options),
                new BanLookup(database, nearMaximum),
                nearMaximum);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                nearMaximumIssuer.Issue(1, "clock-user", 0, "203.0.113.250", remember: true));

            Assert.Equal(1, nearMaximum.Reads);
            Assert.Equal(before, await connection.QuerySingleAsync<MutationSnapshot>(
                "SELECT (SELECT COUNT(*) FROM user_sessions) AS Sessions, (SELECT COUNT(*) FROM user_access_tokens) AS Access, " +
                "(SELECT COUNT(*) FROM user_remember_tokens) AS Remember, ip_last AS Address FROM users WHERE id=1"));
        });
    }

    private static async Task AssertSessionTimes(MySqlConnection connection, AuthSession session, DateTimeOffset now, bool withTicket,
        bool assertSessionCreated = false)
    {
        await AssertTokenTimes(connection, "user_access_tokens", session.AccessToken.Value, now, TimeSpan.FromMinutes(10));
        await AssertTokenTimes(connection, "user_remember_tokens", session.RememberToken!.Value.Value, now, TimeSpan.FromDays(30));

        if (assertSessionCreated) {
            var familyId = await connection.QuerySingleAsync<string>(
                "SELECT family_id FROM user_remember_tokens WHERE token_hash=@hash", new { hash = SecureToken.Hash(session.RememberToken.Value.Value) });
            Assert.Equal(now, await connection.QuerySingleAsync<DateTimeOffset>(
                "SELECT created_at FROM user_sessions WHERE id=@id", new { id = familyId }));
        }

        if (withTicket) {
            Assert.Equal(now.AddMinutes(1), session.SsoTicket.ExpiresAt);
            Assert.Equal(now.AddMinutes(1), await connection.QuerySingleAsync<DateTimeOffset>(
                "SELECT auth_ticket_expires_at FROM users WHERE id=1"));
        }
    }

    private static async Task AssertTokenTimes(MySqlConnection connection, string table, string token, DateTimeOffset now, TimeSpan lifetime)
    {
        var row = await connection.QuerySingleAsync<TokenTimeRow>(
            $"SELECT created_at AS CreatedAt, expires_at AS ExpiresAt FROM `{table}` WHERE token_hash=@hash",
            new { hash = SecureToken.Hash(token) });
        Assert.Equal(now, row.CreatedAt);
        Assert.Equal(now.Add(lifetime), row.ExpiresAt);
    }

    private static async Task VerifyFailedPreflight(string root, string sqlMode, string tombstone, decimal invalid) =>
        await WithSchema(root, async (_, connection) =>
        {
            await SeedDecimalLegacy(connection, sqlMode);
            var update = tombstone switch
            {
                "access.revoked" => "UPDATE user_access_tokens SET revoked_at=@invalid WHERE id=1",
                "remember.used" => "UPDATE user_remember_tokens SET used_at=@invalid WHERE id=1",
                "remember.revoked" => "UPDATE user_remember_tokens SET revoked_at=@invalid WHERE id=1",
                "session.revoked" => "UPDATE user_sessions SET revoked_at=@invalid WHERE id='s'",
                _ => throw new ArgumentOutOfRangeException(nameof(tombstone))
            };
            await connection.ExecuteAsync(update, new { invalid });
            var before = await Snapshot(connection);

            await Assert.ThrowsAnyAsync<MySqlException>(() => connection.ExecuteAsync(Migration));

            Assert.Equal(before, await Snapshot(connection));
        });

    private static async Task VerifySuccessfulDecimalMigration(string root, string sqlMode) =>
        await WithSchema(root, async (builder, connection) =>
        {
            await SeedDecimalLegacy(connection, sqlMode);
            await connection.ExecuteAsync(Migration);
            var instant = await connection.QuerySingleAsync<DateTimeOffset>("SELECT created_at FROM user_sessions WHERE id='s'");
            Assert.Equal(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_560), instant);
            Assert.Equal("datetime", await connection.QuerySingleAsync<string>(
                "SELECT data_type FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='user_access_tokens' AND column_name='expires_at'"));
            Assert.Equal(1, await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND table_name='user_access_tokens' AND index_name='expires_at'"));
            Assert.Equal(10, await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() " +
                "AND ((table_name='users' AND column_name='auth_ticket_expires_at') " +
                "OR (table_name='user_access_tokens' AND column_name IN ('created_at','expires_at','revoked_at')) " +
                "OR (table_name='user_remember_tokens' AND column_name IN ('created_at','expires_at','used_at','revoked_at')) " +
                "OR (table_name='user_sessions' AND column_name IN ('created_at','revoked_at'))) " +
                "AND data_type='datetime' AND datetime_precision=6 AND is_nullable='YES'"));
            Assert.Equal(10, await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND " +
                "((table_name='users' AND column_name='auth_ticket_expires_at' AND ordinal_position=3) OR " +
                "(table_name='user_access_tokens' AND ((column_name='created_at' AND ordinal_position=5) OR (column_name='expires_at' AND ordinal_position=6) OR (column_name='revoked_at' AND ordinal_position=7))) OR " +
                "(table_name='user_remember_tokens' AND ((column_name='created_at' AND ordinal_position=5) OR (column_name='expires_at' AND ordinal_position=6) OR (column_name='used_at' AND ordinal_position=7) OR (column_name='revoked_at' AND ordinal_position=9))) OR " +
                "(table_name='user_sessions' AND ((column_name='created_at' AND ordinal_position=3) OR (column_name='revoked_at' AND ordinal_position=4))))"));
            Assert.Equal(3, await connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema=DATABASE() AND " +
                "((table_name='user_access_tokens' AND index_name='expires_at') OR " +
                "(table_name='user_remember_tokens' AND index_name='expires_at') OR " +
                "(table_name='user_sessions' AND index_name='created_at'))"));
            Assert.Equal(6, await connection.QuerySingleAsync<int>(
                "SELECT (auth_ticket_expires_at IS NULL) FROM users WHERE id=2")
                + await connection.QuerySingleAsync<int>("SELECT (created_at IS NULL)+(expires_at IS NULL) FROM user_access_tokens WHERE id=2")
                + await connection.QuerySingleAsync<int>("SELECT (created_at IS NULL)+(expires_at IS NULL) FROM user_remember_tokens WHERE id=2")
                + await connection.QuerySingleAsync<int>("SELECT (created_at IS NULL) FROM user_sessions WHERE id='unknown'"));

            SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
            var clock = new ManualTime(new DateTimeOffset(2041, 2, 3, 4, 5, 6, TimeSpan.Zero).AddTicks(1_234_560));
            var database = new TestDatabase(builder.ConnectionString);
            var options = AuthTestConfig.Options(c =>
            {
                c.AccessTokenLifetimeMinutes = 10;
                c.RememberTokenLifetimeDays = 30;
                c.RememberReuseGraceSeconds = 30;
                c.SsoTicketLifetimeSeconds = 60;
            });
            var access = await new AccessTokenStore(database, clock, options).Issue(1, "s");
            var remember = await new RememberTokenStore(database, clock, options).Issue(1);
            var ticket = await new SsoTicketStore(database, clock, options).Issue(1);
            Assert.Equal(clock.Now.AddMinutes(10), access.ExpiresAt);
            Assert.Equal(clock.Now.AddDays(30), remember.ExpiresAt);
            Assert.Equal(clock.Now.AddSeconds(60), ticket.ExpiresAt);
            Assert.Equal(clock.Now, await connection.QuerySingleAsync<DateTimeOffset>(
                "SELECT created_at FROM user_access_tokens WHERE token_hash=@hash", new { hash = SecureToken.Hash(access.Value) }));
            Assert.Equal(clock.Now, await connection.QuerySingleAsync<DateTimeOffset>(
                "SELECT created_at FROM user_sessions WHERE id<>'s' ORDER BY created_at DESC LIMIT 1"));
            await connection.ExecuteAsync("UPDATE user_remember_tokens SET used_at=@future WHERE token_hash=@hash",
                new { future = clock.Now.AddDays(1).UtcDateTime, hash = SecureToken.Hash(remember.Value) });
            Assert.Equal(RememberRotationStatus.Rotated, (await new RememberTokenStore(database, clock, options).Rotate(remember.Value)).Status);
            Assert.Null(await new AccessTokenStore(database, clock, options).FindOwner("unknown-access"));
            Assert.Equal(RememberRotationStatus.Invalid,
                (await new RememberTokenStore(database, clock, options).Rotate("unknown-remember")).Status);
        });

    private static async Task SeedDecimalLegacy(MySqlConnection connection, string sqlMode)
    {
        await connection.ExecuteAsync($"SET SESSION sql_mode = '{sqlMode}'; " + LegacySchema);
        await connection.ExecuteAsync("INSERT INTO users VALUES (1, '', 0, 0, NULL, 0); " +
            "INSERT INTO users VALUES (2, '', 0, 0, NULL, 0); " +
            "INSERT INTO user_sessions VALUES ('s',1,2208988800.123456,NULL); " +
            "INSERT INTO user_sessions VALUES ('unknown',2,0,NULL); " +
            "INSERT INTO user_access_tokens VALUES (1,1,'s',REPEAT('a',64),2208988800.123456,2208988860.654321,NULL); " +
            "INSERT INTO user_access_tokens VALUES (2,2,'unknown',SHA2('unknown-access',256),NULL,NULL,NULL); " +
            "INSERT INTO user_remember_tokens VALUES (1,1,'s',REPEAT('b',64),2208988800.123456,2208988860.654321,NULL,0,NULL); " +
            "INSERT INTO user_remember_tokens VALUES (2,2,'unknown',SHA2('unknown-remember',256),NULL,NULL,NULL,0,NULL)");
    }

    private static async Task WithSchema(string root, Func<MySqlConnectionStringBuilder, MySqlConnection, Task> action)
    {
        var schema = "task_refactor_tests_auth_" + Guid.NewGuid().ToString("N");
        var builder = new MySqlConnectionStringBuilder(root) { Database = "", Pooling = false };
        await using var server = new MySqlConnection(builder.ConnectionString);
        await server.OpenAsync();
        await server.ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET latin1");

        try {
            builder.Database = schema;
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await action(builder, connection);
        }
        finally {
            await server.ExecuteAsync($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private static string Repo(string path) => Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../", path));
    private static string Migration => File.ReadAllText(Repo("Database/Migrations/40_UseUtcCredentialTimes.sql"));

    private static async Task<string> Snapshot(MySqlConnection connection)
    {
        var definitions = new List<string>();

        foreach (var table in new[] { "users", "user_access_tokens", "user_remember_tokens", "user_sessions" }) {
            var row = await connection.QuerySingleAsync<dynamic>($"SHOW CREATE TABLE `{table}`");
            definitions.Add((string)((IDictionary<string, object>)row).Values.Last());
        }

        var rows = new List<string>();
        rows.AddRange(await connection.QueryAsync<string>(
            "SELECT CONCAT('users:',id,'|',auth_ticket,'|',COALESCE(CAST(auth_ticket_expires_at AS CHAR),'<NULL>'),'|',credential_generation,'|',COALESCE(auth_ticket_session,'<NULL>'),'|',auth_ticket_exchanged) FROM users ORDER BY id"));
        rows.AddRange(await connection.QueryAsync<string>(
            "SELECT CONCAT('access:',id,'|',user_id,'|',COALESCE(session_id,'<NULL>'),'|',token_hash,'|',COALESCE(CAST(created_at AS CHAR),'<NULL>'),'|',COALESCE(CAST(expires_at AS CHAR),'<NULL>'),'|',COALESCE(CAST(revoked_at AS CHAR),'<NULL>')) FROM user_access_tokens ORDER BY id"));
        rows.AddRange(await connection.QueryAsync<string>(
            "SELECT CONCAT('remember:',id,'|',user_id,'|',family_id,'|',token_hash,'|',COALESCE(CAST(created_at AS CHAR),'<NULL>'),'|',COALESCE(CAST(expires_at AS CHAR),'<NULL>'),'|',COALESCE(CAST(used_at AS CHAR),'<NULL>'),'|',grace_uses,'|',COALESCE(CAST(revoked_at AS CHAR),'<NULL>')) FROM user_remember_tokens ORDER BY id"));
        rows.AddRange(await connection.QueryAsync<string>(
            "SELECT CONCAT('sessions:',id,'|',user_id,'|',COALESCE(CAST(created_at AS CHAR),'<NULL>'),'|',COALESCE(CAST(revoked_at AS CHAR),'<NULL>')) FROM user_sessions ORDER BY id"));

        return string.Join("\n", definitions.Concat(rows));
    }

    private sealed class TestDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }

    private sealed class TokenTimeRow
    {
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed record MutationSnapshot
    {
        public int Sessions { get; set; }
        public int Access { get; set; }
        public int Remember { get; set; }
        public string Address { get; set; } = "";
    }

    private sealed class AdvancingTime(DateTimeOffset start, TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _next = start;
        public int Reads { get; private set; }
        public DateTimeOffset LastUtc { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            var value = _next;
            _next += step;
            LastUtc = value.ToUniversalTime();

            return value;
        }

        public void ResetReads() => Reads = 0;
    }

    private const string LegacySchema = """
        CREATE TABLE users (id INT PRIMARY KEY, auth_ticket VARCHAR(64) NOT NULL, auth_ticket_expires_at DECIMAL(20,6) NULL,
            credential_generation INT NOT NULL, auth_ticket_session CHAR(32) NULL, auth_ticket_exchanged BOOL NOT NULL);
        CREATE TABLE user_sessions (id CHAR(32) PRIMARY KEY, user_id INT NOT NULL, created_at DECIMAL(20,6) NOT NULL,
            revoked_at DECIMAL(20,6) NULL, KEY user_id(user_id), KEY created_at(created_at));
        CREATE TABLE user_access_tokens (id BIGINT AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL, session_id CHAR(32) NULL, token_hash CHAR(64) NOT NULL,
            created_at DECIMAL(20,6) NULL, expires_at DECIMAL(20,6) NULL, revoked_at DECIMAL(20,6) NULL,
            UNIQUE KEY token_hash(token_hash), KEY user_id(user_id), KEY session_id(session_id), KEY expires_at(expires_at));
        CREATE TABLE user_remember_tokens (id BIGINT AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL, family_id CHAR(32) NOT NULL, token_hash CHAR(64) NOT NULL,
            created_at DECIMAL(20,6) NULL, expires_at DECIMAL(20,6) NULL, used_at DECIMAL(20,6) NULL, grace_uses TINYINT NOT NULL DEFAULT 0,
            revoked_at DECIMAL(20,6) NULL, UNIQUE KEY token_hash(token_hash), KEY user_id(user_id), KEY family_id(family_id), KEY expires_at(expires_at));
        """;
}
