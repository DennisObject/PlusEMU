using System.Data;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Rooms.Polls;
using Xunit;

namespace Plus.Tests;

public sealed class RoomPollDatabaseFactAttribute : FactAttribute
{
    public RoomPollDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ROOM_POLL_DATABASE") is null) {
            Skip = "Opt-in isolated room poll MariaDB probe.";
        }
    }
}

public sealed class RoomPollDatabaseTests
{
    [RoomPollDatabaseFact]
    public async Task ResponsesResumeWithDistinctValuesCompleteOnceAndRollbackOnWriteFailure()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_POLL_DATABASE")!)
        {
            Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true
        };
        using var connection = new MySqlConnection(options.ConnectionString);
        connection.Open();
        var schema = "room_polls_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try {
            connection.Execute($"USE `{schema}`");
            var migration = File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/46_RoomPolls.sql"));
            connection.Execute(migration);
            connection.Execute(migration);
            connection.Execute("""
                INSERT INTO room_polls (id, room_id, type, title, summary, end_message, nps)
                VALUES (10,42,'CLIENT_NPS','Survey','Opinion','Thanks',TRUE);
                INSERT INTO room_poll_questions (id,poll_id,parent_id,sort_order,type,text,category,answer_type,choices) VALUES
                    (1,10,0,1,1,'Recommend?',0,0,'[{"Value":"1","Label":"Yes","Category":1},{"Value":"0","Label":"No","Category":0}]'),
                    (2,10,0,2,2,'Select',0,0,'[{"Value":"10","Label":"One","Category":0},{"Value":"20","Label":"Two","Category":0}]'),
                    (3,10,1,1,3,'Why?',1,0,'[]');
                """);
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(options.ConnectionString) { Database = schema }.ConnectionString);
            var store = new RoomPollStore(database);
            var poll = Assert.IsType<RoomPollSnapshot>(store.Load(42));
            Assert.Null(store.Load(43));
            Assert.Equal(new[] { 1, 2 }, poll.Questions.Select(question => question.Id));
            Assert.False(store.Answer(poll, 7, 3, ["Forged followup"], DateTimeOffset.UtcNow));
            Assert.False(store.Answer(poll, 7, 99, ["Foreign question"], DateTimeOffset.UtcNow));
            Assert.False(store.Answer(poll, 7, 1, ["crafted"], DateTimeOffset.UtcNow));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_poll_responses"));
            Assert.True(store.Answer(poll, 7, 1, ["1"], DateTimeOffset.UtcNow));
            Assert.False(store.Completed(10, 7));
            // A new adapter models reconnect/resume; every submitted checkbox value stays distinct.
            store = new RoomPollStore(database);
            Assert.True(store.Answer(poll, 7, 2, ["10", "20"], DateTimeOffset.UtcNow));
            Assert.False(store.Completed(10, 7));
            connection.Execute("CREATE TRIGGER refuse_poll BEFORE UPDATE ON room_poll_responses FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='failure'");
            Assert.Throws<MySqlException>(() => store.Answer(poll, 7, 3, ["Good"], DateTimeOffset.UtcNow));
            Assert.False(store.Completed(10, 7));
            Assert.DoesNotContain(3, Answers(connection, 7).Keys);
            connection.Execute("DROP TRIGGER refuse_poll");
            var now = new DateTimeOffset(2042, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(330)).AddTicks(1234560);
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => store.Answer(poll, 7, 3, ["Good"], now))));
            Assert.Single(results, accepted => accepted);
            Assert.True(store.Completed(10, 7));
            Assert.False(store.Answer(poll, 7, 1, ["0"], now));
            Assert.Equal(new[] { "10", "20" }, Answers(connection, 7)[2]);
            Assert.Equal(now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.ffffff"), connection.ExecuteScalar<string>("SELECT DATE_FORMAT(completed_at, '%Y-%m-%d %H:%i:%s.%f') FROM room_poll_responses WHERE user_id=7"));
            Assert.True(store.Answer(poll, 8, 1, ["0"], now));
            Assert.True(store.Answer(poll, 8, 2, [], now));
            Assert.True(store.Completed(10, 8));
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_poll_responses WHERE completed_at IS NOT NULL"));
        }
        finally {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private static Dictionary<int, string[]> Answers(MySqlConnection connection, int userId) =>
        JsonSerializer.Deserialize<Dictionary<int, string[]>>(connection.ExecuteScalar<string>(
            "SELECT answers FROM room_poll_responses WHERE user_id=@userId", new { userId }))!;

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
