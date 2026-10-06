using System.Text;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Subscriptions;
using Xunit;

namespace Plus.Tests;

public sealed class ClubMigrationDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_CLUB_MIGRATION_SERVER_CONNECTION_STRING";
    public ClubMigrationDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) == null)
        {
            Skip = $"Set {Variable} to a bare MariaDB server connection (no Database).";
        }
    }
}

public sealed class ClubMigrationDatabaseTheoryAttribute : TheoryAttribute
{
    public ClubMigrationDatabaseTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable(ClubMigrationDatabaseFactAttribute.Variable) == null)
        {
            Skip = $"Set {ClubMigrationDatabaseFactAttribute.Variable} to a bare MariaDB server connection (no Database).";
        }
    }
}

/// <summary>A per-test GUID schema created from the bare server connection and dropped when the test ends.</summary>
internal sealed class ClubMigrationSchema : IDisposable
{
    private readonly string _server;
    public string Name { get; } = "task_acl_tests_migration38_" + Guid.NewGuid().ToString("N")[..12];
    public string ConnectionString
    {
        get;
    }

    public ClubMigrationSchema(string server)
    {
        _server = server;

        using (var admin = new MySqlConnection(server))
        {
            admin.Open();
            admin.Execute($"CREATE DATABASE `{Name}` CHARACTER SET utf8mb4");
        }

        ConnectionString = new MySqlConnectionStringBuilder(server) { Database = Name, AllowZeroDateTime = true, ConvertZeroDateTime = true, SslMode = MySqlSslMode.None, Pooling = false }.ToString();
    }

    public void Dispose()
    {
        using var admin = new MySqlConnection(_server);
        admin.Open();
        admin.Execute($"DROP DATABASE IF EXISTS `{Name}`");
    }
}

/// <summary>
/// Runs Database/Migrations/38_UseUtcClubTimes.sql against the pre-migration club tables, with the same MySQL options as
/// production (AllowZeroDateTime, ConvertZeroDateTime). The source tables are seeded with the legacy BIGINT shapes.
/// </summary>
[Collection("ClubDatabase")]
public sealed class ClubUtcMigrationDatabaseTests
{
    private const string Migration = "Database/Migrations/38_UseUtcClubTimes.sql";
    private static readonly string[] Tables = ["user_club_memberships", "club_membership_intervals", "club_paydays", "club_credit_spending", "club_gift_claims"];

    private static readonly string[] LegacyDdl =
    [
        "DROP TABLE IF EXISTS club_gift_claims, club_credit_spending, club_paydays, club_membership_intervals, user_club_memberships",
        """
        CREATE TABLE user_club_memberships (
          user_id INT NOT NULL PRIMARY KEY, expires_at BIGINT NOT NULL,
          started_at BIGINT NOT NULL DEFAULT 0, first_started_at BIGINT NOT NULL DEFAULT 0,
          past_seconds BIGINT NOT NULL DEFAULT 0, modified_at BIGINT NOT NULL DEFAULT 0,
          gifts_claimed INT NOT NULL DEFAULT 0) ENGINE=InnoDB
        """,
        """
        CREATE TABLE club_membership_intervals (
          user_id INT NOT NULL, started_at BIGINT NOT NULL, expires_at BIGINT NOT NULL,
          PRIMARY KEY (user_id, started_at)) ENGINE=InnoDB
        """,
        """
        CREATE TABLE club_gift_claims (
          id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL, gift_number INT NOT NULL,
          catalog_item_id INT NOT NULL, claimed_at BIGINT NOT NULL, UNIQUE KEY (user_id, gift_number)) ENGINE=InnoDB
        """,
        """
        CREATE TABLE club_credit_spending (
          id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL, credits INT NOT NULL,
          spent_at BIGINT NOT NULL, KEY (user_id, spent_at)) ENGINE=InnoDB
        """,
        """
        CREATE TABLE club_paydays (
          user_id INT NOT NULL, payday BIGINT NOT NULL, spent INT NOT NULL, streak_bonus INT NOT NULL,
          spending_bonus INT NOT NULL, paid TINYINT(1) NOT NULL, PRIMARY KEY(user_id, payday)) ENGINE=InnoDB
        """,
    ];

    // Legacy seconds: 1001 has every field, 1002 has only zeros (no real time), 1003 sits at 2039.
    private const string LegacySeed = """
        INSERT INTO user_club_memberships (user_id, expires_at, started_at, first_started_at, past_seconds, modified_at, gifts_claimed) VALUES
          (1001, 1800000000, 1700000000, 1600000000, 86400, 1700000500, 2), (1002, 0, 0, 0, 0, 0, 0), (1003, 2177452800, 2177452800, 2177452800, 5, 2177452800, 1);
        INSERT INTO club_membership_intervals (user_id, started_at, expires_at) VALUES (1001, 1700000000, 1800000000), (1003, 2177452800, 2177539200);
        INSERT INTO club_paydays (user_id, payday, spent, streak_bonus, spending_bonus, paid) VALUES (1001, 1698796800, 0, 5, 0, 1), (1001, 1701388800, 10, 0, 1, 0), (1003, 2177452800, 0, 0, 0, 1);
        INSERT INTO club_credit_spending (user_id, credits, spent_at) VALUES (1001, 50, 1700000100), (1001, 20, 0), (1003, 10, 2177452800);
        INSERT INTO club_gift_claims (user_id, gift_number, catalog_item_id, claimed_at) VALUES (1001, 1, 1017, 1700000200), (1001, 2, 1017, 0);
        """;

    private static string Server => Environment.GetEnvironmentVariable(ClubMigrationDatabaseFactAttribute.Variable)!;

    [ClubMigrationDatabaseTheory]
    [InlineData("", "interval")]
    [InlineData("", "payday")]
    [InlineData("", "interval-past-9999")]
    [InlineData("STRICT_TRANS_TABLES", "interval")]
    [InlineData("STRICT_TRANS_TABLES", "payday")]
    [InlineData("STRICT_TRANS_TABLES", "interval-past-9999")]
    public void MalformedKeysFailBeforeAnyStagingAndLeaveEverySourceTableUntouched(string sqlMode, string malformed)
    {
        using var schema = new ClubMigrationSchema(Server);
        using var connection = new MySqlConnection(schema.ConnectionString);
        connection.Open();
        Legacy(connection);
        connection.Execute($"SET SESSION sql_mode = '{sqlMode}'");
        connection.Execute(malformed switch
        {
            "interval" => "INSERT INTO club_membership_intervals (user_id, started_at, expires_at) VALUES (1009, 0, 1800000000)",
            "payday" => "INSERT INTO club_paydays (user_id, payday, spent, streak_bonus, spending_bonus, paid) VALUES (1009, -1, 0, 0, 0, 0)",
            _ => "INSERT INTO club_membership_intervals (user_id, started_at, expires_at) VALUES (1009, 253402300800, 253402300900)",
        });
        var before = Snapshot(connection);

        Assert.ThrowsAny<MySqlException>(() => connection.Execute(Script()));

        Assert.Equal(before, Snapshot(connection));
    }

    [ClubMigrationDatabaseTheory]
    [InlineData("")]
    [InlineData("STRICT_TRANS_TABLES")]
    public void ProductionOptionsMaterializeConvertedInstantsAndPreserveTheKeyContract(string sqlMode)
    {
        using var schema = new ClubMigrationSchema(Server);
        using var connection = new MySqlConnection(schema.ConnectionString);
        connection.Open();
        Legacy(connection);
        connection.Execute($"SET SESSION sql_mode = '{sqlMode}'");

        connection.Execute(Script());

        var zero = connection.QuerySingle<ClubMembershipRow>("SELECT expires_at AS ExpiresAt, started_at AS StartedAt, first_started_at AS FirstStartedAt, past_seconds AS PastSeconds, modified_at AS ModifiedAt, gifts_claimed AS GiftsClaimed FROM user_club_memberships WHERE user_id = 1002").ToMembership();
        Assert.Null(zero.ExpiresAt);
        Assert.Null(zero.StartedAt);
        Assert.Null(zero.FirstStartedAt);
        Assert.Null(zero.ModifiedAt);
        var member = connection.QuerySingle<ClubMembershipRow>("SELECT expires_at AS ExpiresAt, started_at AS StartedAt, first_started_at AS FirstStartedAt, past_seconds AS PastSeconds, modified_at AS ModifiedAt, gifts_claimed AS GiftsClaimed FROM user_club_memberships WHERE user_id = 1001").ToMembership();
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000), member.ExpiresAt);
        Assert.Equal(TimeSpan.Zero, member.ExpiresAt!.Value.Offset);
        Assert.Equal(86400, member.PastSeconds);
        Assert.Equal(2, member.GiftsClaimed);
        var far = connection.QuerySingle<ClubMembershipRow>("SELECT expires_at AS ExpiresAt, started_at AS StartedAt, first_started_at AS FirstStartedAt, past_seconds AS PastSeconds, modified_at AS ModifiedAt, gifts_claimed AS GiftsClaimed FROM user_club_memberships WHERE user_id = 1003").ToMembership();
        Assert.Equal(new DateTimeOffset(2039, 1, 1, 0, 0, 0, TimeSpan.Zero), far.StartedAt);

        Assert.Equal(new DateTimeOffset(2023, 11, 14, 22, 15, 0, TimeSpan.Zero), connection.ExecuteScalar<DateTimeOffset?>("SELECT spent_at FROM club_credit_spending WHERE id = 1"));
        Assert.Null(connection.ExecuteScalar<DateTimeOffset?>("SELECT spent_at FROM club_credit_spending WHERE id = 2"));
        Assert.Equal(new DateTimeOffset(2039, 1, 1, 0, 0, 0, TimeSpan.Zero), connection.ExecuteScalar<DateTimeOffset?>("SELECT payday FROM club_paydays WHERE user_id = 1003"));
        Assert.Equal(new DateTimeOffset(2023, 11, 14, 22, 16, 40, TimeSpan.Zero), connection.ExecuteScalar<DateTimeOffset?>("SELECT claimed_at FROM club_gift_claims WHERE id = 1"));
        Assert.Null(connection.ExecuteScalar<DateTimeOffset?>("SELECT claimed_at FROM club_gift_claims WHERE id = 2"));

        // Runtime writes keep microseconds through the same loader.
        connection.Execute("INSERT INTO user_club_memberships (user_id, expires_at, started_at, first_started_at, past_seconds, modified_at, gifts_claimed) VALUES (1004, '2030-06-01 12:00:00.123456', NULL, NULL, 0, NULL, 0)");
        var fraction = connection.QuerySingle<ClubMembershipRow>("SELECT expires_at AS ExpiresAt, started_at AS StartedAt, first_started_at AS FirstStartedAt, past_seconds AS PastSeconds, modified_at AS ModifiedAt, gifts_claimed AS GiftsClaimed FROM user_club_memberships WHERE user_id = 1004").ToMembership();
        Assert.Equal(new DateTimeOffset(2030, 6, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_560), fraction.ExpiresAt);

        AssertMetadata(connection);
    }

    [ClubMigrationDatabaseTheory]
    [InlineData("")]
    [InlineData("STRICT_TRANS_TABLES")]
    public void OutOfRangeNonKeyInstantsBecomeNullInEverySqlMode(string sqlMode)
    {
        using var schema = new ClubMigrationSchema(Server);
        using var connection = new MySqlConnection(schema.ConnectionString);
        connection.Open();
        Legacy(connection);
        // Past 9999-12-31 (253402300799) and far beyond it: legacy BIGINT can hold these, the migration cannot store them.
        connection.Execute("""
            INSERT INTO user_club_memberships (user_id, expires_at, started_at, first_started_at, past_seconds, modified_at, gifts_claimed) VALUES (1010, 253402300800, 1700000000, 1700000000, 3, 300000000000, 1);
            INSERT INTO club_gift_claims (user_id, gift_number, catalog_item_id, claimed_at) VALUES (1010, 1, 1017, 99999999999999);
            INSERT INTO club_credit_spending (user_id, credits, spent_at) VALUES (1010, 7, 253402300800);
            """);
        connection.Execute($"SET SESSION sql_mode = '{sqlMode}'");

        connection.Execute(Script());

        var row = connection.QuerySingle<ClubMembershipRow>("SELECT expires_at AS ExpiresAt, started_at AS StartedAt, first_started_at AS FirstStartedAt, past_seconds AS PastSeconds, modified_at AS ModifiedAt, gifts_claimed AS GiftsClaimed FROM user_club_memberships WHERE user_id = 1010").ToMembership();
        Assert.Null(row.ExpiresAt);
        Assert.Null(row.ModifiedAt);
        Assert.Equal(new DateTimeOffset(2023, 11, 14, 22, 13, 20, TimeSpan.Zero), row.StartedAt);
        Assert.Equal(3, row.PastSeconds);
        Assert.Equal(1, row.GiftsClaimed);
        Assert.Null(connection.ExecuteScalar<DateTimeOffset?>("SELECT claimed_at FROM club_gift_claims WHERE user_id = 1010"));
        Assert.Null(connection.ExecuteScalar<DateTimeOffset?>("SELECT spent_at FROM club_credit_spending WHERE user_id = 1010"));
        Assert.Equal(7, connection.ExecuteScalar<int>("SELECT credits FROM club_credit_spending WHERE user_id = 1010"));
    }

    // The shipped legacy columns are BIGINT, so fractions cannot occur there. This variant widens the legacy epoch columns to
    // DECIMAL(20,6) only to prove the staging expression keeps microseconds exactly; it does not model a real legacy schema.
    [ClubMigrationDatabaseFact]
    public void FractionalLegacyEpochsRoundTripMicrosecondsThroughTheStagingExpression()
    {
        using var schema = new ClubMigrationSchema(Server);
        using var connection = new MySqlConnection(schema.ConnectionString);
        connection.Open();
        Legacy(connection);
        connection.Execute("ALTER TABLE user_club_memberships MODIFY expires_at DECIMAL(20,6) NOT NULL, MODIFY started_at DECIMAL(20,6) NOT NULL");
        connection.Execute("UPDATE user_club_memberships SET expires_at = 1800000000.123456, started_at = 1700000000.654321 WHERE user_id = 1001");
        connection.Execute(Script());

        var row = connection.QuerySingle<ClubMembershipRow>("SELECT expires_at AS ExpiresAt, started_at AS StartedAt, first_started_at AS FirstStartedAt, past_seconds AS PastSeconds, modified_at AS ModifiedAt, gifts_claimed AS GiftsClaimed FROM user_club_memberships WHERE user_id = 1001").ToMembership();
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1800000000).AddTicks(1_234_560), row.ExpiresAt);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000).AddTicks(6_543_210), row.StartedAt);
    }

    [ClubMigrationDatabaseFact]
    public void ConvertedTablesKeepTheirKeysAndNullableInstantsAfterAFreshMigration()
    {
        using var schema = new ClubMigrationSchema(Server);
        using var connection = new MySqlConnection(schema.ConnectionString);
        connection.Open();
        Legacy(connection);
        connection.Execute(Script());
        AssertMetadata(connection);
    }

    private static void AssertMetadata(MySqlConnection connection)
    {
        var columns = connection.Query<(string Table, string Column, string Type, string Nullable)>(
            "SELECT TABLE_NAME AS `Table`, COLUMN_NAME AS `Column`, COLUMN_TYPE AS `Type`, IS_NULLABLE AS `Nullable` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND (COLUMN_NAME LIKE '%_at' OR COLUMN_NAME = 'payday') ORDER BY TABLE_NAME, COLUMN_NAME").ToArray();
        Assert.Equal(["club_credit_spending.spent_at:datetime(6):YES", "club_gift_claims.claimed_at:datetime(6):YES",
            "club_membership_intervals.expires_at:datetime(6):YES", "club_membership_intervals.started_at:datetime(6):NO",
            "club_paydays.payday:datetime(6):NO", "user_club_memberships.expires_at:datetime(6):YES",
            "user_club_memberships.first_started_at:datetime(6):YES", "user_club_memberships.modified_at:datetime(6):YES",
            "user_club_memberships.started_at:datetime(6):YES"],
            columns.Select(column => $"{column.Table}.{column.Column}:{column.Type}:{column.Nullable}").ToArray());

        var keys = connection.Query<string>("SELECT CONCAT(TABLE_NAME, '.', INDEX_NAME, '(', GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX), ')') FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME IN ('club_membership_intervals', 'club_paydays', 'club_credit_spending', 'club_gift_claims') GROUP BY TABLE_NAME, INDEX_NAME ORDER BY TABLE_NAME, INDEX_NAME").ToArray();
        Assert.Equal(["club_credit_spending.PRIMARY(id)", "club_credit_spending.user_id(user_id,spent_at)", "club_gift_claims.PRIMARY(id)",
            "club_gift_claims.user_id(user_id,gift_number)", "club_membership_intervals.PRIMARY(user_id,started_at)", "club_paydays.PRIMARY(user_id,payday)"], keys);
    }

    private static string Script() => File.ReadAllText(HabbiconPacketTests.Repo(Migration));

    private static void Legacy(MySqlConnection connection)
    {
        foreach (var statement in LegacyDdl)
        {
            connection.Execute(statement);
        }

        connection.Execute(LegacySeed);
    }

    // Every source table's column definitions and rows, rendered in primary-key order.
    private static string Snapshot(MySqlConnection connection)
    {
        var builder = new StringBuilder();

        foreach (var table in Tables)
        {
            builder.Append(table).Append('\n');

            foreach (var column in connection.Query<string>("SELECT CONCAT(COLUMN_NAME, ' ', COLUMN_TYPE, ' ', IS_NULLABLE, ' ', COALESCE(COLUMN_DEFAULT, 'NULL'), ' ', EXTRA) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION", new
            {
                table
            }))
            {
                builder.Append(column).Append('\n');
            }

            foreach (var index in connection.Query<string>("SELECT CONCAT(INDEX_NAME, ' ', NON_UNIQUE, ' ', COLUMN_NAME, ' ', SEQ_IN_INDEX) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table ORDER BY INDEX_NAME, SEQ_IN_INDEX", new
            {
                table
            }))
            {
                builder.Append(index).Append('\n');
            }

            using var reader = connection.ExecuteReader($"SELECT * FROM {table} ORDER BY 1, 2");

            while (reader.Read())
            {
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    builder.Append(reader.GetValue(i) is DBNull ? "NULL" : reader.GetValue(i)).Append('|');
                }

                builder.Append('\n');
            }
        }

        return builder.ToString();
    }
}
