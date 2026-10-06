using Dapper;
using MySqlConnector;
using Plus.Database;
using Xunit;

namespace Plus.Tests;

[Collection("ClubDatabase")]
public sealed class UtcMigrationRangeGuardTests
{
    private const string ServerVariable = "ROOM_COMPONENT_DATABASE";
    private const long FirstUnrepresentableSecond = 253402300800;
    private const long MillisecondsLimit = 253402300799999;

    [RoomComponentDatabaseFact]
    public void NullableUtcMigrationsGuardUnrepresentableValuesInEverySqlMode()
    {
        SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());

        foreach (var sqlMode in new[] { "", "STRICT_ALL_TABLES" }) {
            foreach (var migration in Cases()) {
                Verify(migration, sqlMode, widened: false);

                if (migration.Widen != null) {
                    Verify(migration, sqlMode, widened: true);
                }
            }
        }
    }

    private static void Verify(MigrationCase migration, string sqlMode, bool widened)
    {
        var root = Environment.GetEnvironmentVariable(ServerVariable)!;
        var schema = $"task_utc_range_{migration.Number}_{Guid.NewGuid():N}";
        var adminBuilder = new MySqlConnectionStringBuilder(root) { Database = "", Pooling = false };
        using var admin = new MySqlConnection(adminBuilder.ConnectionString);
        admin.Open();
        admin.Execute($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4");

        try {
            var builder = new MySqlConnectionStringBuilder(root)
            {
                Database = schema,
                Pooling = false,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true,
                SslMode = MySqlSslMode.None
            };
            using var connection = new MySqlConnection(builder.ConnectionString);
            connection.Open();
            connection.Execute($"SET SESSION sql_mode = '{sqlMode}'");
            connection.Execute(migration.Setup);

            if (widened) {
                connection.Execute(migration.Widen!);
            }

            connection.Execute(widened ? migration.WideSeed : migration.ActualSeed);
            var before = migration.Targets.Select(target => target.Table).Distinct().ToDictionary(table => table,
                table => connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM `{table}`"));

            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo($"Database/Migrations/{migration.File}")));

            foreach (var target in migration.Targets) {
                Assert.Equal(before[target.Table], connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM `{target.Table}`"));
                var values = connection.Query<DateTimeOffset?>($"SELECT `{target.Column}` FROM `{target.Table}` ORDER BY {target.OrderBy}").ToArray();

                for (var index = 0; index < values.Length; index++) {
                    var expected = index == 1 ? (widened ? target.WideValidAt : target.ActualValidAt)
                        : migration.Number == 37 && index == 5 ? DateTimeOffset.FromUnixTimeMilliseconds(MillisecondsLimit)
                        : migration.Number == 34 && !widened && target.Column == "expire" && index == 3
                            ? DateTimeOffset.UnixEpoch.AddSeconds(1) : (DateTimeOffset?)null;
                    Assert.True(expected == values[index],
                        $"{migration.File}:{target.Table}.{target.Column} row {index + 1} expected {expected:O}, got {values[index]:O}");
                }

                var metadata = connection.QuerySingle<ColumnMetadata>("""
                    SELECT COLUMN_TYPE AS Type, IS_NULLABLE AS Nullable, ORDINAL_POSITION AS Ordinal
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @Table AND COLUMN_NAME = @Column
                    """, new { target.Table, target.Column });
                Assert.Equal("datetime(6)", metadata.Type);
                Assert.Equal("YES", metadata.Nullable);
                Assert.True(target.Ordinal == metadata.Ordinal,
                    $"{migration.File}:{target.Table}.{target.Column} expected ordinal {target.Ordinal}, got {metadata.Ordinal}");
            }

            foreach (var index in migration.Indexes) {
                Assert.Equal(index.Columns, connection.QuerySingle<string>("""
                    SELECT GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX)
                    FROM information_schema.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @Table AND INDEX_NAME = @Name
                    """, new { index.Table, index.Name }));
            }
        }
        finally {
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private static IEnumerable<MigrationCase> Cases()
    {
        yield return One(20, "20_UseUtcPetCreationTime.sql", "bots_petdata", "createstamp", "INT NULL", 2);
        yield return Two(21, "21_UseUtcRoomPromotionTimes.sql", "room_promotions", "timestamp_start", "timestamp_expire", "DOUBLE NOT NULL", 2, 3, true);
        yield return RoomBanCase();
        yield return Two(23, "23_UseUtcRoomVisitTimes.sql", "user_roomvisits", "entry_timestamp", "exit_timestamp", "DOUBLE NOT NULL", 4, 5, true,
            [new("user_roomvisits", "entry_timestamp", "entry_timestamp"), new("user_roomvisits", "exit_timestamp", "exit_timestamp")]);
        yield return One(24, "24_UseUtcChatlogTimes.sql", "chatlogs", "timestamp", "DOUBLE NOT NULL", 2, true);
        yield return One(25, "25_UseUtcMarketplaceTimes.sql", "catalog_marketplace_offers", "timestamp", "DOUBLE NOT NULL", 2, true,
            finalColumn: "listed_at");
        yield return One(26, "26_UseUtcGroupCreationTime.sql", "groups", "created", "INT NOT NULL", 2);
        yield return Two(27, "27_UseUtcRewardTimes.sql", "server_rewards", "reward_start", "reward_end", "INT NOT NULL", 2, 3);
        yield return Two(28, "28_UseUtcQuestDefinitionTimes.sql", "quests", "timestamp_unlock", "timestamp_lock", "INT NOT NULL", 2, 3);
        yield return One(29, "29_UseUtcAmbassadorLogTimes.sql", "ambassador_logs", "timestamp", "DOUBLE NOT NULL", 2, true);
        yield return One(30, "30_UseUtcNameChangeLogTimes.sql", "logs_client_namechange", "timestamp", "INT NOT NULL", 2);
        yield return One(33, "33_UseUtcRoomInvitationTimes.sql", "chatlogs_console_invitations", "timestamp", "DOUBLE NOT NULL", 2, true);
        yield return BanCase();
        yield return RewardTrackCase();
        yield return AuditCase();
        yield return HabbiconCase();
    }

    private static MigrationCase One(int number, string file, string table, string column, string type, int ordinal,
        bool supportsOverflow = false, IndexExpectation[]? indexes = null, string? finalColumn = null)
    {
        var actualValid = type.StartsWith("INT", StringComparison.Ordinal) ? "2147483647" : "2200000000.123456";
        var actualSeed = $"INSERT INTO `{table}` (id,`{column}`) VALUES (1,0),(2,{actualValid})" +
            (supportsOverflow ? $",(3,{FirstUnrepresentableSecond}),(4,1e100)" : "") + ";";

        return new(number, file,
            $"CREATE TABLE `{table}` (id INT PRIMARY KEY{PrefixColumns(ordinal)}, `{column}` {type}{IndexSql(indexes)}) ENGINE=InnoDB",
            actualSeed,
            $"ALTER TABLE `{table}` MODIFY `{column}` DECIMAL(30,6) NULL",
            $"INSERT INTO `{table}` (id,`{column}`) VALUES (1,0),(2,2200000000.123456),(3,{FirstUnrepresentableSecond}),(4,NULL),(5,-1)",
            [TargetFor(table, finalColumn ?? column, ordinal, type, supportsOverflow)], indexes ?? []);
    }

    private static MigrationCase Two(int number, string file, string table, string first, string second, string type,
        int firstOrdinal, int secondOrdinal, bool supportsOverflow = false, IndexExpectation[]? indexes = null)
    {
        var actualValid = type.StartsWith("INT", StringComparison.Ordinal) ? "2147483647" : "2200000000.123456";
        var rows = $"(1,0,0),(2,{actualValid},{actualValid})" +
            (supportsOverflow ? $",(3,{FirstUnrepresentableSecond},{FirstUnrepresentableSecond}),(4,1e100,1e100)" : "");

        return new(number, file,
            $"CREATE TABLE `{table}` (id INT PRIMARY KEY{PrefixColumns(firstOrdinal)}, `{first}` {type}, `{second}` {type}{IndexSql(indexes)}) ENGINE=InnoDB",
            $"INSERT INTO `{table}` (id,`{first}`,`{second}`) VALUES {rows}",
            $"ALTER TABLE `{table}` MODIFY `{first}` DECIMAL(30,6) NULL, MODIFY `{second}` DECIMAL(30,6) NULL",
            $"INSERT INTO `{table}` (id,`{first}`,`{second}`) VALUES (1,0,0),(2,2200000000.123456,2200000000.123456),(3,{FirstUnrepresentableSecond},{FirstUnrepresentableSecond}),(4,NULL,NULL),(5,-1,-1)",
            [TargetFor(table, first, firstOrdinal, type, supportsOverflow), TargetFor(table, second, secondOrdinal, type, supportsOverflow)], indexes ?? []);
    }

    private static MigrationCase BanCase() => new(34, "34_UseUtcModerationBanTimes.sql",
        "CREATE TABLE bans (id INT PRIMARY KEY, expire DOUBLE NOT NULL, added_date VARCHAR(50) NOT NULL)",
        $"INSERT INTO bans VALUES (1,0,'0'),(2,2200000000.123456,'2200000000.123456'),(3,{FirstUnrepresentableSecond},'{FirstUnrepresentableSecond}'),(4,1,'not-a-time'),(5,1e100,'99999999999999999999999999999999999999999999999999')",
        "ALTER TABLE bans MODIFY expire DECIMAL(30,6) NULL",
        $"INSERT INTO bans VALUES (1,0,'0'),(2,2200000000.123456,'2200000000.123456'),(3,{FirstUnrepresentableSecond},'{FirstUnrepresentableSecond}'),(4,NULL,'not-a-time'),(5,-1,'-1')",
        [TargetFor("bans", "expire", 2, "DOUBLE", true), TargetFor("bans", "added_date", 3, "DOUBLE", true)], []);

    private static MigrationCase RoomBanCase() => new(22, "22_UseUtcRoomBanExpiry.sql",
        "CREATE TABLE room_bans (user_id INT NOT NULL, room_id INT NOT NULL, expire INT NOT NULL, PRIMARY KEY(user_id,room_id), KEY user_id(user_id), KEY room_id(room_id))",
        "INSERT INTO room_bans VALUES (1,1,0),(2,1,2147483647)",
        "ALTER TABLE room_bans MODIFY expire DECIMAL(30,6) NULL",
        $"INSERT INTO room_bans VALUES (1,1,0),(2,1,2200000000.123456),(3,1,{FirstUnrepresentableSecond}),(4,1,NULL),(5,1,-1)",
        [new("room_bans", "expire", "user_id,room_id", 3,
            DateTimeOffset.FromUnixTimeSeconds(2147483647),
            DateTimeOffset.FromUnixTimeSeconds(2200000000).AddTicks(1_234_560))],
        [new("room_bans", "user_id", "user_id"), new("room_bans", "room_id", "room_id")]);

    private static MigrationCase RewardTrackCase() => new(35, "35_UseUtcRewardTrackTimes.sql",
        """
        CREATE TABLE reward_tracks (id INT PRIMARY KEY, starts_at INT NOT NULL, ends_at INT NOT NULL);
        CREATE TABLE users_reward_track_prizes (id INT PRIMARY KEY, claimed_at INT NOT NULL)
        """,
        "INSERT INTO reward_tracks VALUES (1,0,0),(2,2147483647,2147483647); INSERT INTO users_reward_track_prizes VALUES (1,0),(2,2147483647)",
        "ALTER TABLE reward_tracks MODIFY starts_at DECIMAL(30,6) NULL, MODIFY ends_at DECIMAL(30,6) NULL; ALTER TABLE users_reward_track_prizes MODIFY claimed_at DECIMAL(30,6) NULL",
        $"INSERT INTO reward_tracks VALUES (1,0,0),(2,2200000000.123456,2200000000.123456),(3,{FirstUnrepresentableSecond},{FirstUnrepresentableSecond}),(4,NULL,NULL),(5,-1,-1); INSERT INTO users_reward_track_prizes VALUES (1,0),(2,2200000000.123456),(3,{FirstUnrepresentableSecond}),(4,NULL),(5,-1)",
        [TargetFor("reward_tracks", "starts_at", 2, "INT"), TargetFor("reward_tracks", "ends_at", 3, "INT"), TargetFor("users_reward_track_prizes", "claimed_at", 2, "INT")], []);

    private static MigrationCase AuditCase() => new(36, "36_UseUtcAuditLogTimes.sql",
        """
        CREATE TABLE logs_client_staff (id INT PRIMARY KEY, `timestamp` DOUBLE NOT NULL);
        CREATE TABLE housekeeping_log (id INT PRIMARY KEY, `timestamp` INT NOT NULL, action VARCHAR(64) NOT NULL, KEY timestamp_action (`timestamp`,action))
        """,
        $"INSERT INTO logs_client_staff VALUES (1,0),(2,2200000000.123456),(3,{FirstUnrepresentableSecond}),(4,1e100); INSERT INTO housekeeping_log VALUES (1,0,'a'),(2,2147483647,'b')",
        "ALTER TABLE logs_client_staff MODIFY `timestamp` DECIMAL(30,6) NULL; ALTER TABLE housekeeping_log MODIFY `timestamp` DECIMAL(30,6) NULL",
        $"INSERT INTO logs_client_staff VALUES (1,0),(2,2200000000.123456),(3,{FirstUnrepresentableSecond}),(4,NULL),(5,-1); INSERT INTO housekeeping_log VALUES (1,0,'a'),(2,2200000000.123456,'b'),(3,{FirstUnrepresentableSecond},'c'),(4,NULL,'d'),(5,-1,'e')",
        [TargetFor("logs_client_staff", "timestamp", 2, "DOUBLE", true), TargetFor("housekeeping_log", "timestamp", 2, "INT")], [new("housekeeping_log", "timestamp_action", "timestamp,action")]);

    private static MigrationCase HabbiconCase() => new(37, "37_UseUtcHabbiconUsageTimes.sql",
        "CREATE TABLE users_habbicons (user_id INT NOT NULL, habbicon_id INT NOT NULL, last_used BIGINT NULL, PRIMARY KEY(user_id,habbicon_id), KEY recent(user_id,last_used))",
        $"INSERT INTO users_habbicons VALUES (1,1,0),(2,1,2200000000123),(3,1,{MillisecondsLimit + 1}),(4,1,NULL),(5,1,-1),(6,1,{MillisecondsLimit})", null, "",
        [new("users_habbicons", "last_used", "user_id,habbicon_id", 3,
            DateTimeOffset.FromUnixTimeMilliseconds(2200000000123), DateTimeOffset.FromUnixTimeMilliseconds(2200000000123), true)],
        [new("users_habbicons", "recent", "user_id,last_used")]);

    private static Target TargetFor(string table, string column, int ordinal, string type, bool actualSupportsOverflow = false)
    {
        var integer = type.StartsWith("INT", StringComparison.Ordinal);

        return new(table, column, "id", ordinal,
            DateTimeOffset.FromUnixTimeSeconds(integer ? 2147483647 : 2200000000).AddTicks(integer ? 0 : 1_234_560),
            DateTimeOffset.FromUnixTimeSeconds(2200000000).AddTicks(1_234_560), actualSupportsOverflow);
    }

    private static string IndexSql(IndexExpectation[]? indexes) => indexes == null ? "" : string.Concat(indexes.Select(index => $", KEY `{index.Name}` ({index.Columns})"));

    private static string PrefixColumns(int targetOrdinal) => string.Concat(
        Enumerable.Range(2, targetOrdinal - 2).Select(ordinal => $", preceding_{ordinal} INT NOT NULL DEFAULT 0"));

    private sealed record MigrationCase(int Number, string File, string Setup, string ActualSeed, string? Widen,
        string WideSeed, Target[] Targets, IndexExpectation[] Indexes);
    private sealed record Target(string Table, string Column, string OrderBy, int Ordinal,
        DateTimeOffset ActualValidAt, DateTimeOffset WideValidAt, bool ActualSupportsOverflow = false);
    private sealed record IndexExpectation(string Table, string Name, string Columns);
    private sealed class ColumnMetadata
    {
        public string Type { get; set; } = ""; public string Nullable { get; set; } = ""; public int Ordinal { get; set; }
    }
}
