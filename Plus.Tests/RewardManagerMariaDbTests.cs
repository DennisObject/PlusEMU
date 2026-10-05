using System.Data;
using System.Reflection;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Rewards;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RewardManagerMariaDbTests
{
    [Fact]
    public async Task LoadsDateTime6AndAppliesExactBoundaryUsingCapturedUtcClock()
    {
        var connectionString = Environment.GetEnvironmentVariable("PLUS_REWARD_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var database = new RawDatabase(connectionString);
        var boundary = new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1234560);
        using (var connection = database.Connection())
        {
            connection.Execute("DROP TABLE IF EXISTS server_reward_logs; DROP TABLE IF EXISTS server_rewards");
            connection.Execute("CREATE TABLE server_rewards(id INT PRIMARY KEY,reward_start DECIMAL(20,6) NULL,reward_end DECIMAL(20,6) NULL,reward_type VARCHAR(20) NOT NULL,reward_data VARCHAR(255) NOT NULL,message VARCHAR(255) NOT NULL,enabled BOOL NOT NULL)");
            connection.Execute("CREATE TABLE server_reward_logs(user_id INT NOT NULL,reward_id INT NOT NULL)");
            var epoch = boundary.ToUnixTimeSeconds() + boundary.Millisecond / 1000m + boundary.Microsecond / 1_000_000m;
            connection.Execute("INSERT INTO server_rewards VALUES(1,@epoch,@epoch,'credits','5','boundary',TRUE),(2,NULL,@epoch,'credits','99','missing',TRUE)", new { epoch });
            connection.Execute(File.ReadAllText(MigrationPath()));
        }

        var manager = new RewardManager(database, Proxy<IBadgeManager>(), new FixedTimeProvider(boundary));
        await manager.Start();
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7, Credits = 10 });

        await manager.CheckRewards(client);

        Assert.Equal(15, client.GetHabbo().Credits);
        using var verify = database.Connection();
        Assert.Equal(1, verify.ExecuteScalar<int>("SELECT COUNT(*) FROM server_reward_logs WHERE user_id=7 AND reward_id=1"));
        Assert.Equal(0, verify.ExecuteScalar<int>("SELECT COUNT(*) FROM server_reward_logs WHERE user_id=7 AND reward_id=2"));
    }

    private static string MigrationPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "Database", "Migrations", "27_UseUtcRewardTimes.sql");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Could not find reward schedule migration.");
    }

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, EmptyProxy>();
    public class EmptyProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType == typeof(Task) ? Task.CompletedTask : null;
    }
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class RawDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public Plus.Database.Interfaces.IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
        public bool IsConnected() => true;
    }
}
