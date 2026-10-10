using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.UserData;
using Xunit;

namespace Plus.Tests;

// users.online is set when an SSO login registers its session and cleared by that session's logout, never by a replaced one.
public sealed class UserOnlineFlagTests
{
    private const int UserId = 7;

    [Fact]
    public async Task ASuccessfulLoginMarksItsRegisteredSessionOnline()
    {
        var clients = new SharedTestClients();
        var marked = new List<(GameClient, int, bool)>();
        var persistence = CatalogSnapshotTestSupport.Proxy<IUserPersistenceService>((_, args) =>
        {
            var session = (GameClient)args[0]!;
            marked.Add((session, (int)args[1]!, ReferenceEquals(clients.GetClientByUserId(UserId), session)));

            return null;
        });
        var (session, _) = HabbiconTestSupport.Client(null!);

        Assert.Null(await Login(clients, persistence, _ => { }).AuthenticateUsingSSO(session, "ticket-for-user-seven"));
        Assert.Equal([(session, UserId, true)], marked);
    }

    [Fact]
    public async Task ALoginWhoseConnectionClosesBeforeRegisteringNeverMarksOnline()
    {
        var clients = new SharedTestClients();
        var persistence = CatalogSnapshotTestSupport.Proxy<IUserPersistenceService>((name, _) => throw new InvalidOperationException(name));
        var (session, _) = HabbiconTestSupport.Client(null!);

        Assert.Equal(AuthenticationError.SessionClosed,
            await Login(clients, persistence, _ => session.Disconnect()).AuthenticateUsingSSO(session, "ticket-for-user-seven"));
        Assert.Null(clients.GetClientByUserId(UserId));
    }

    [RoomComponentDatabaseFact]
    public void TheSessionsOwnLogoutClearsTheFlagAndStampsLastOnline()
    {
        InSchema((connection, database) =>
        {
            var clients = new SharedTestClients();
            var persistence = new UserPersistenceService(database, TimeProvider.System, clients);
            var (habbo, session) = Session(clients);

            persistence.MarkOnline(session, UserId);
            Assert.Equal((true, false), State(connection));

            persistence.Save(habbo);
            Assert.Equal((false, true), State(connection));
        });
    }

    [RoomComponentDatabaseFact]
    public void AClosedOrReplacedSessionIsNeverMarkedOnline()
    {
        InSchema((connection, database) =>
        {
            var clients = new SharedTestClients();
            var persistence = new UserPersistenceService(database, TimeProvider.System, clients);
            var (_, closed) = Session(clients);
            closed.Disconnect();
            persistence.MarkOnline(closed, UserId);
            Assert.False(State(connection).Online);

            var (_, replaced) = Session(clients);
            Session(clients);
            persistence.MarkOnline(replaced, UserId);
            Assert.False(State(connection).Online);
        });
    }

    [RoomComponentDatabaseFact]
    public void AReplacedSessionsLogoutLeavesTheNewLoginOnline()
    {
        InSchema((connection, database) =>
        {
            var clients = new SharedTestClients();
            var persistence = new UserPersistenceService(database, TimeProvider.System, clients);
            var (first, firstSession) = Session(clients);
            persistence.MarkOnline(firstSession, UserId);

            // The second login registers before the first session's off-thread logout saves.
            var (second, secondSession) = Session(clients);
            persistence.MarkOnline(secondSession, UserId);
            persistence.Save(first);
            Assert.True(State(connection).Online);

            persistence.Save(second);
            Assert.False(State(connection).Online);
        });
    }

    [RoomComponentDatabaseFact]
    public async Task AReloginRacingTheOldLogoutAlwaysEndsOnline()
    {
        await InSchemaAsync(async (connection, database) =>
        {
            GameClient? registered = null;
            var persistence = new UserPersistenceService(database, TimeProvider.System, new TestGameClientManager(_ => Volatile.Read(ref registered)));

            for (var i = 0; i < 25; i++) {
                var (old, oldSession) = Session(new SharedTestClients());
                Volatile.Write(ref registered, oldSession);
                persistence.MarkOnline(oldSession, UserId);
                oldSession.Disconnect();
                var (next, _) = HabbiconTestSupport.Client(null!);
                var logout = Task.Run(() => persistence.Save(old));
                var login = Task.Run(() =>
                {
                    Volatile.Write(ref registered, next);
                    persistence.MarkOnline(next, UserId);
                });
                await Task.WhenAll(logout, login);

                Assert.True(State(connection).Online);
            }
        });
    }

    private static Authenticator Login(IGameClientManager clients, IUserPersistenceService persistence, Action<Habbo> loaded)
    {
        var tickets = CatalogSnapshotTestSupport.Proxy<ISsoTicketStore>((name, _) => name == nameof(ISsoTicketStore.Consume)
            ? Task.FromResult<int?>(UserId)
            : throw new NotSupportedException(name));
        var users = CatalogSnapshotTestSupport.Proxy<IUserDataFactory>((name, _) =>
        {
            if (name != nameof(IUserDataFactory.Create)) {
                throw new NotSupportedException(name);
            }

            var habbo = new Habbo { Id = UserId, Username = "seven", Access = UserAccess.Empty };
            loaded(habbo);

            return Task.FromResult<Habbo?>(habbo);
        });

        return new Authenticator([], clients, users, tickets, new AccountSessionGate(), persistence);
    }

    private static (Habbo Habbo, GameClient Session) Session(SharedTestClients clients)
    {
        var habbo = new Habbo
        {
            Id = UserId,
            SessionStartedAt = DateTimeOffset.UtcNow,
            HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0)
        };
        var (session, _) = HabbiconTestSupport.Client(habbo);
        habbo.Client = session;
        clients.Online[UserId] = session;

        return (habbo, session);
    }

    private static (bool Online, bool LastOnlineStamped) State(MySqlConnection connection) =>
        connection.QuerySingle<(bool, bool)>("SELECT online, last_online IS NOT NULL FROM users WHERE id = @UserId", new { UserId });

    private static void InSchema(Action<MySqlConnection, HabbiconDatabaseTests.TestDatabase> run) =>
        InSchemaAsync((connection, database) =>
        {
            run(connection, database);

            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private static async Task InSchemaAsync(Func<MySqlConnection, HabbiconDatabaseTests.TestDatabase, Task> run)
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        {
            AllowUserVariables = true,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_user_online_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");

        try {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            var install = File.ReadAllText(HabbiconPacketTests.Repo("Database/FreshInstall.sql"));

            // Only these tables are copied, so their keys to rooms and groups have nothing to check against.
            connection.Execute("SET FOREIGN_KEY_CHECKS = 0");

            foreach (var table in new[] { "users", "users_settings", "user_statistics", "user_currencies" }) {
                connection.Execute(Regex.Match(install, $@"CREATE TABLE `{table}` \([\s\S]*?\) ENGINE=[^;]+;").Value);
            }

            connection.Execute("INSERT INTO users (id, username, online) VALUES (@UserId, 'seven', false); " +
                "INSERT INTO users_settings (user_id) VALUES (@UserId); INSERT INTO user_statistics (id) VALUES (@UserId)", new { UserId });
            await run(connection, new HabbiconDatabaseTests.TestDatabase(options.ConnectionString));
        }
        finally {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
