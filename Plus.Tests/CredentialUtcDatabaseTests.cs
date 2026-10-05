using Dapper;
using MySqlConnector;
using System.Data;
using Plus.Database;
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
        var builder = new MySqlConnectionStringBuilder(root) { Database = "" };
        await using var server = new MySqlConnection(builder.ConnectionString);
        await server.OpenAsync();
        await server.ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET latin1");
        try
        {
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
        finally
        {
            await server.ExecuteAsync($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    [RoomComponentDatabaseFact]
    public async Task MigrationRejectsInvalidTombstonesBeforeAlterAndPreservesNativeInstants()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        foreach (var sqlMode in new[] { "STRICT_ALL_TABLES", "" })
        foreach (var invalid in new[] { 0m, -1m, 253402300800m })
        {
            var schema = "task_refactor_tests_auth_" + Guid.NewGuid().ToString("N");
            var builder = new MySqlConnectionStringBuilder(root) { Database = "" };
            await using var server = new MySqlConnection(builder.ConnectionString);
            await server.OpenAsync();
            await server.ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET latin1");
            try
            {
                builder.Database = schema;
                await using var connection = new MySqlConnection(builder.ConnectionString);
                await connection.OpenAsync();
                await connection.ExecuteAsync($"SET SESSION sql_mode = '{sqlMode}'; " + LegacySchema);
                await connection.ExecuteAsync("INSERT INTO users VALUES (1, '', 0, 0, NULL, 0); " +
                    "INSERT INTO users VALUES (2, '', 0, 0, NULL, 0); " +
                    "INSERT INTO user_sessions VALUES ('s',1,2208988800.123456,@invalid); " +
                    "INSERT INTO user_sessions VALUES ('unknown',2,0,NULL); " +
                    "INSERT INTO user_access_tokens VALUES (1,1,'s',REPEAT('a',64),2208988800.123456,2208988860.654321,@invalid); " +
                    "INSERT INTO user_access_tokens VALUES (2,2,'unknown',SHA2('unknown-access',256),NULL,NULL,NULL); " +
                    "INSERT INTO user_remember_tokens VALUES (1,1,'s',REPEAT('b',64),2208988800.123456,2208988860.654321,@invalid,0,@invalid); " +
                    "INSERT INTO user_remember_tokens VALUES (2,2,'unknown',SHA2('unknown-remember',256),NULL,NULL,NULL,0,NULL)",
                    new { invalid });

                var migration = File.ReadAllText(Repo("Database/Migrations/40_UseUtcCredentialTimes.sql"));
                var before = await Snapshot(connection);
                await Assert.ThrowsAnyAsync<MySqlException>(() => connection.ExecuteAsync(migration));
                Assert.Equal(before, await Snapshot(connection));

                await connection.ExecuteAsync("UPDATE user_sessions SET revoked_at=NULL; UPDATE user_access_tokens SET revoked_at=NULL; " +
                    "UPDATE user_remember_tokens SET used_at=NULL,revoked_at=NULL");
                await connection.ExecuteAsync(migration);
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
            }
            finally
            {
                builder.Database = "";
                await server.ExecuteAsync($"DROP DATABASE IF EXISTS `{schema}`");
            }
        }
    }

    private static string Repo(string path) => Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../", path));

    private static async Task<string> Snapshot(MySqlConnection connection)
    {
        var definitions = new List<string>();
        foreach (var table in new[] { "users", "user_access_tokens", "user_remember_tokens", "user_sessions" })
        {
            var row = await connection.QuerySingleAsync<dynamic>($"SHOW CREATE TABLE `{table}`");
            definitions.Add((string)((IDictionary<string, object>)row).Values.Last());
        }
        var values = await connection.QuerySingleAsync<string>(
            "SELECT CONCAT_WS('|', u.auth_ticket_expires_at, a.created_at, a.expires_at, a.revoked_at, " +
            "r.created_at, r.expires_at, r.used_at, r.revoked_at, s.created_at, s.revoked_at) " +
            "FROM users u JOIN user_access_tokens a ON a.user_id=u.id JOIN user_remember_tokens r ON r.user_id=u.id " +
            "JOIN user_sessions s ON s.user_id=u.id WHERE u.id=1");
        return string.Join("\n", definitions.Append(values));
    }

    private sealed class TestDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
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
