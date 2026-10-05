using System.Buffers.Binary;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Communication.Packets.Incoming.FriendList;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public class MessengerUtcHabiconTests
{
    [Fact]
    public void WireSecondsClampsBelowEpochAndPastInt32AndKeepsWholeSeconds()
    {
        Assert.Equal(0, MessengerTime.WireSeconds(At(new DateTime(1969, 12, 31, 23, 59, 59, DateTimeKind.Utc))));
        Assert.Equal(0, MessengerTime.WireSeconds(At(DateTime.UnixEpoch)));
        Assert.Equal(1_700_000_000, MessengerTime.WireSeconds(At(new DateTime(2023, 11, 14, 22, 13, 20, 123, DateTimeKind.Utc))));
        Assert.Equal(int.MaxValue, MessengerTime.WireSeconds(At(new DateTime(2038, 1, 19, 3, 14, 8, DateTimeKind.Utc))));
        Assert.Equal(int.MaxValue, MessengerTime.WireSeconds(At(new DateTime(9999, 12, 31, 23, 59, 59, 999, DateTimeKind.Utc))));
    }

    [Fact]
    public void MissingTimesHaveNoWireValueAndReadAsZeroSecondsAgo()
    {
        var now = new DateTime(2023, 11, 14, 22, 15, 0, DateTimeKind.Utc);
        Assert.Equal(0, MessengerTime.WireSeconds((DateTimeOffset?)null));
        Assert.Equal(0, MessengerTime.SecondsBetween(now, (DateTimeOffset?)null));
    }

    [Fact]
    public void SecondsBetweenReadsFutureAsZeroAndClampsOverflow()
    {
        var now = At(new DateTime(2023, 11, 14, 22, 15, 0, DateTimeKind.Utc));
        Assert.Equal(0, MessengerTime.SecondsBetween(now, now.AddSeconds(5)));
        Assert.Equal(60, MessengerTime.SecondsBetween(now, now.AddSeconds(-60.5)));
        Assert.Equal(int.MaxValue, MessengerTime.SecondsBetween(now, At(new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
    }

    [Fact]
    public async Task EventDecodesSixPrimitiveFieldsAndDelegatesWithoutSending()
    {
        var messenger = new RecordingMessengerService();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });

        await new SendMessengerMessageEvent(messenger).Parse(client, HabbiconTestSupport.Incoming(0, 2, 7, 4, "61", ""));

        Assert.Equal(new object[] { 0, 2, 7, 4, "61", "" }, Assert.Single(messenger.Calls));
        Assert.Empty(sent);
    }

    [Fact]
    public void AcknowledgesWithStoredUtcTimeAfterOneClockReadAndStoreCommit()
    {
        var clock = new CountingClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new RecordingStore(messageId: 42);
        var (client, sent) = Sender(clock);
        var readsBeforeSend = clock.Reads;

        Service(store, clock).Send(client, 0, 2, 7, 4, "61", "");

        var record = Assert.Single(store.Records);
        Assert.Equal((1, 2, ":toast_toast:", new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc), true), record);
        Assert.Equal(1, clock.Reads - readsBeforeSend);
        Assert.Equal(new[] { ServerPacketHeader.MessengerMessageAckComposer, ServerPacketHeader.UserHabbiconsComposer }, sent.Select(packet => packet.Header));
        var ack = sent[0].Payload;
        Assert.Equal((7, 0, 42, 1_700_000_000), (ReadInt(ack, 0), ReadInt(ack, 4), ReadInt(ack, 8), ReadInt(ack, 12)));
    }

    [Fact]
    public void StoreFailureSendsOnlyTheFailureCodeAndNoAck()
    {
        var clock = new CountingClock(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        var store = new RecordingStore(failure: UnreachableServerException());
        var (client, sent) = Sender(clock);

        Service(store, clock).Send(client, 0, 2, 7, 4, "61", "");

        var failed = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.MessengerMessageFailedComposer, failed.Header);
        Assert.Equal((7, 7), (ReadInt(failed.Payload, 0), ReadInt(failed.Payload, 4)));
    }

    [MessengerUtcDatabaseFact]
    public async Task MigrationConvertsLegacySecondsToUtcMicrosecondsWithClamps()
    {
        await WithSchema(async connectionString =>
        {
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            CreateLegacyTables(connection);
            connection.Execute("INSERT INTO chatlogs_console (from_id, to_id, message, `timestamp`) VALUES (1, 2, 'zero', 0), (1, 2, 'negative', -5), (1, 2, 'fraction', 1700000000.123456), (1, 2, 'future', 2500000000.25), (1, 2, 'huge', 1e20), (1, 2, 'edge', 253402300799.5)");
            connection.Execute("INSERT INTO messenger_offline_messages (to_id, from_id, message, `timestamp`) VALUES (2, 1, 'old', 0), (2, 1, 'later', 2200000000.5)");
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/31_UseUtcMessengerTimes.sql")));

            var console = connection.Query<(string Message, string? Stamp)>("SELECT message AS Message, CAST(`timestamp` AS CHAR) AS Stamp FROM chatlogs_console ORDER BY id").ToList();
            Assert.Equal(new (string, string?)[]
            {
                ("zero", null),
                ("negative", null),
                ("fraction", "2023-11-14 22:13:20.123456"),
                ("future", "2049-03-22 04:26:40.250000"),
                ("huge", null),
                ("edge", null),
            }, console);
            var offline = connection.Query<(string Message, string? Stamp)>("SELECT message AS Message, CAST(`timestamp` AS CHAR) AS Stamp FROM messenger_offline_messages ORDER BY id").ToList();
            Assert.Equal(new (string, string?)[] { ("old", null), ("later", "2039-09-18 23:06:40.500000") }, offline);
            Assert.Equal(("datetime", "YES", 6), Column(connection, "chatlogs_console"));
            Assert.Equal(("datetime", "YES", 6), Column(connection, "messenger_offline_messages"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='chatlogs_console' AND INDEX_NAME='timestamp'"));
        });
    }

    [MessengerUtcDatabaseFact]
    public async Task StoreRecordsUtcBeyond2038WithMicrosecondsAndRollsBackWhenOfflineInsertFails()
    {
        await WithSchema(async connectionString =>
        {
            CreateMigratedSchema(connectionString);
            var store = new HabbiconMessengerStore(new HabbiconDatabaseTests.TestDatabase(connectionString));
            var createdAt = new DateTime(2040, 12, 31, 20, 0, 0, DateTimeKind.Utc).AddTicks(1_234_560);

            var messageId = store.Record(1, 2, ":toast:", createdAt, true);

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(messageId, connection.ExecuteScalar<int>("SELECT MAX(id) FROM chatlogs_console"));
            Assert.Equal("2040-12-31 20:00:00.123456", connection.ExecuteScalar<string>("SELECT CAST(`timestamp` AS CHAR) FROM chatlogs_console WHERE from_id = 1"));
            Assert.Equal("2040-12-31 20:00:00.123456", connection.ExecuteScalar<string>("SELECT CAST(`timestamp` AS CHAR) FROM messenger_offline_messages WHERE from_id = 1"));

            Assert.Throws<MySqlException>(() => store.Record(3, 4, new string('x', 300), createdAt, true));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM chatlogs_console WHERE from_id = 3"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM messenger_offline_messages WHERE from_id = 3"));
        });
    }

    [MessengerUtcDatabaseFact]
    public async Task LegacyConsoleLogsAndOfflineWritesUseTheInjectedClock()
    {
        await WithSchema(async connectionString =>
        {
            CreateMigratedSchema(connectionString);
            var clock = new FixedTimeProvider(new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(5_000_000));
            var loader = new MessengerDataLoader(new HabbiconDatabaseTests.TestDatabase(connectionString), null!, null!, null!, clock);

            await loader.LogPrivateMessage(1, 2, "logged");
            await loader.LogPrivateOfflineMessage(1, 2, "queued");

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal("2040-01-02 03:04:05.500000", connection.ExecuteScalar<string>("SELECT CAST(`timestamp` AS CHAR) FROM chatlogs_console WHERE message = 'logged'"));
            Assert.Equal("2040-01-02 03:04:05.500000", connection.ExecuteScalar<string>("SELECT CAST(`timestamp` AS CHAR) FROM messenger_offline_messages WHERE message = 'queued'"));
        });
    }

    [MessengerUtcDatabaseFact]
    public async Task OfflineReadGroupsInOrderAndDeletesOnlyTheRowsItRead()
    {
        await WithSchema(async connectionString =>
        {
            CreateMigratedSchema(connectionString);
            using (var setup = new MySqlConnection(connectionString))
            {
                setup.Open();
                setup.Execute("""
                    INSERT INTO messenger_offline_messages (to_id, from_id, message, `timestamp`) VALUES
                    (2, 1, 'a', '2023-11-14 22:13:20.500000'),
                    (2, 3, 'b', '2023-11-14 22:13:30.000000'),
                    (2, 1, 'c', '2023-11-14 22:13:25.250000'),
                    (2, 1, 'unknown', NULL),
                    (9, 1, 'other', '2023-11-14 22:13:20.000000')
                    """);
            }
            var loader = new MessengerDataLoader(new HabbiconDatabaseTests.TestDatabase(connectionString), null!, null!, null!,
                new FixedTimeProvider(DateTimeOffset.FromUnixTimeSeconds(1_700_000_100)));

            var result = await loader.GetAndDeleteOfflineMessages(2);

            Assert.Equal(new[] { 1, 3 }, result.Keys);
            Assert.Equal(new[] { ("unknown", 0), ("a", 99), ("c", 94) }, result[1]);
            Assert.Equal(new[] { ("b", 90) }, result[3]);
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM messenger_offline_messages WHERE to_id = 2"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM messenger_offline_messages WHERE to_id = 9"));
        });
    }

    [MessengerUtcDatabaseFact]
    public async Task OfflineReadMaterializesNativeDateTimeBeyond2038AndNullsThroughTheLoader()
    {
        await WithSchema(async connectionString =>
        {
            CreateMigratedSchema(connectionString);
            using (var setup = new MySqlConnection(connectionString))
            {
                setup.Open();
                setup.Execute("INSERT INTO messenger_offline_messages (to_id, from_id, message, `timestamp`) VALUES (2, 3, 'after 2038', '2039-12-31 23:59:50.250000'), (2, 3, 'unknown', NULL)");
            }
            var loader = new MessengerDataLoader(new HabbiconDatabaseTests.TestDatabase(connectionString), null!, null!, null!,
                new FixedTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero)));

            var result = await loader.GetAndDeleteOfflineMessages(2);

            Assert.Equal(new[] { ("unknown", 0), ("after 2038", 9) }, result[3]);
        });
    }

    [MessengerUtcDatabaseFact]
    public async Task ExactIdDeleteLeavesRowsThatArrivedAfterTheRead()
    {
        await WithSchema(async connectionString =>
        {
            CreateMigratedSchema(connectionString);
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("INSERT INTO messenger_offline_messages (to_id, from_id, message, `timestamp`) VALUES (2, 1, 'read', '2023-11-14 22:13:20.000000'), (2, 1, 'arrived later', '2023-11-14 22:13:21.000000')");
            var readId = connection.ExecuteScalar<int>("SELECT id FROM messenger_offline_messages WHERE message = 'read'");

            using var transaction = connection.BeginTransaction();
            await MessengerDataLoader.DeleteReadOfflineMessages(connection, transaction, new[] { readId });
            transaction.Commit();

            Assert.Equal(new[] { "arrived later" }, connection.Query<string>("SELECT message FROM messenger_offline_messages WHERE to_id = 2"));
        });
    }

    // A real MySqlException from a socket that does not exist, so the failure path sees the production exception type.
    private static MySqlException UnreachableServerException()
    {
        using var connection = new MySqlConnection("Server=/tmp/task-messenger-missing-socket/mariadb.sock;Connect Timeout=1");
        try
        {
            connection.Open();
        }
        catch (MySqlException exception)
        {
            return exception;
        }
        throw new InvalidOperationException("Expected the missing socket to be unreachable.");
    }

    private static DateTimeOffset At(DateTime utc) => new(utc);

    private static int ReadInt(byte[] payload, int offset) => BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(offset, 4));

    // The messenger and the service share one clock, so the operation's reads can be counted in isolation.
    private static (GameClient Client, List<(uint Header, byte[] Payload)> Sent) Sender(TimeProvider clock)
    {
        var sender = new Habbo { Id = 1, Messenger = new HabboMessenger(new() { [2] = new MessengerBuddy { Id = 2 } }, new(), new(), clock) };
        var (client, sent) = HabbiconTestSupport.Client(sender);
        return (client, sent);
    }

    private static HabbiconMessengerService Service(IHabbiconMessengerStore store, TimeProvider clock) =>
        new(new HabbiconTestSupport.Service(), new GameClientManager(null!, null!), store, NullLogger<HabbiconMessengerService>.Instance, clock);

    private static (string Type, string Nullable, int Precision) Column(MySqlConnection connection, string table) =>
        connection.QuerySingle<(string, string, int)>("SELECT DATA_TYPE, IS_NULLABLE, DATETIME_PRECISION FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table AND COLUMN_NAME='timestamp'", new { table });

    private static void CreateLegacyTables(MySqlConnection connection)
    {
        connection.Execute("CREATE TABLE chatlogs_console (id INT PRIMARY KEY AUTO_INCREMENT, from_id INT UNSIGNED NOT NULL, to_id INT UNSIGNED NOT NULL, message TEXT NOT NULL, `timestamp` DOUBLE NOT NULL, KEY `timestamp` (`timestamp`)) ENGINE=InnoDB");
        connection.Execute("CREATE TABLE messenger_offline_messages (id INT PRIMARY KEY AUTO_INCREMENT, to_id INT UNSIGNED NOT NULL DEFAULT 0, from_id INT UNSIGNED NOT NULL DEFAULT 0, message VARCHAR(255) NOT NULL, `timestamp` DOUBLE NOT NULL DEFAULT 0) ENGINE=InnoDB");
    }

    private static void CreateMigratedSchema(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        CreateLegacyTables(connection);
        connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/31_UseUtcMessengerTimes.sql")));
    }

    private static async Task WithSchema(Func<string, Task> body)
    {
        var server = Environment.GetEnvironmentVariable("PLUS_MESSENGER_UTC_TEST_CONNECTION_STRING")!;
        var schema = "task_messenger_tests_utc_" + Guid.NewGuid().ToString("N")[..12];
        // Production Database.Connection sets these, and they change how native DATETIME values are materialised.
        var options = new MySqlConnectionStringBuilder(server) { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true };
        using (var admin = new MySqlConnection(server))
        {
            admin.Open();
            admin.Execute($"CREATE DATABASE `{schema}`");
        }
        try
        {
            await body(options.ConnectionString);
        }
        finally
        {
            using var admin = new MySqlConnection(server);
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private sealed class CountingClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return now;
        }
    }

    private sealed class RecordingStore(int messageId = 0, Exception? failure = null) : IHabbiconMessengerStore
    {
        public List<(int Sender, int Recipient, string Fallback, DateTime CreatedAtUtc, bool Offline)> Records { get; } = new();
        public int Record(int senderId, int recipientId, string fallback, DateTime createdAtUtc, bool deliverOffline)
        {
            Records.Add((senderId, recipientId, fallback, createdAtUtc, deliverOffline));
            if (failure != null) throw failure;
            return messageId;
        }
    }

    private sealed class RecordingMessengerService : IHabbiconMessengerService
    {
        public List<object[]> Calls { get; } = new();
        public void Send(GameClient session, int conversationId, int recipientId, int confirmationId, int type, string message, string metadata) =>
            Calls.Add(new object[] { conversationId, recipientId, confirmationId, type, message, metadata });
    }
}

public sealed class MessengerUtcDatabaseFactAttribute : Xunit.FactAttribute
{
    public MessengerUtcDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_MESSENGER_UTC_TEST_CONNECTION_STRING")))
            Skip = "Set PLUS_MESSENGER_UTC_TEST_CONNECTION_STRING to a server that can create and drop disposable task_messenger_tests_utc_ schemas.";
    }
}
