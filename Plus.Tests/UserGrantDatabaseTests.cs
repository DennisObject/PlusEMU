using System.Text;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Authentication.Tasks;
using Plus.HabboHotel.Users.Grants;
using Plus.HabboHotel.Users.UserData;
using Xunit;

namespace Plus.Tests;

public sealed class RconGrantDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_RCON_GRANT_TEST_CONNECTION_STRING";
    public RconGrantDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) == null) {
            Skip = $"Set {Variable} to a disposable task_rcon_grants_tests_ database loaded from Original Database.sql.";
        }
    }
}

[CollectionDefinition("RconGrantDatabase", DisableParallelization = true)]
public sealed class RconGrantDatabaseCollection;

/// <summary>CMS grants over the real schema, gate, session registry and persistence; only item definitions and access are faked.</summary>
[Collection("RconGrantDatabase")]
public sealed class UserGrantDatabaseTests : IDisposable
{
    // Every pause and wait is bounded, so a regression fails the test instead of hanging the run.
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(10);
    private const int User = 962001, Other = 962002, Room = 962101;
    private const uint Chair = 962201, Locked = 962202;
    private readonly HabbiconDatabaseTests.TestDatabase _database;
    private readonly UserGrantStore _store;
    private readonly AccountSessionGate _gate = new(TimeSpan.FromSeconds(20));
    private readonly GameClientManager _clients = new(null!, null!);
    private readonly Dictionary<uint, ItemDefinition> _definitions;
    private readonly IItemDataManager _items;
    private readonly IAccessControl _access;
    private readonly HashSet<string> _rights = [];

    public UserGrantDatabaseTests()
    {
        var connection = Environment.GetEnvironmentVariable(RconGrantDatabaseFactAttribute.Variable)!;

        if (!new MySqlConnectionStringBuilder(connection).Database.StartsWith("task_rcon_grants_tests_", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Disposable database required.");
        }

        _database = new(connection);
        _store = new(_database);
        _definitions = new Dictionary<uint, ItemDefinition>
        {
            [Chair] = new() { Id = Chair, ItemName = "rcon_chair", ProductType = "s", AllowGift = true, InteractionType = InteractionType.None },
            [Locked] = new() { Id = Locked, ItemName = "rcon_locked", ProductType = "s", AllowGift = false, InteractionType = InteractionType.None },
        };
        _items = CatalogSnapshotTestSupport.Proxy<IItemDataManager>((name, _) => name == "get_Items" ? _definitions : throw new InvalidOperationException(name));
        _access = CatalogSnapshotTestSupport.Proxy<IAccessControl>((name, args) => name == "Can" ? _rights.Contains((string)args[1]!) : throw new InvalidOperationException(name));
        Clean();
        Sql("INSERT INTO users (id, username, auth_ticket, credits) VALUES (962001, 'rcon_grant_user', '', 100), (962002, 'rcon_grant_other', '', 100); " +
            "INSERT INTO users_settings (user_id) VALUES (962001), (962002); INSERT INTO user_statistics (id) VALUES (962001), (962002); " +
            "INSERT INTO user_currencies (user_id, type, amount) VALUES (962001, 0, 10); " +
            "INSERT INTO badge_definitions (code, required_right) VALUES ('RCONT1', ''), ('RCONOWN', ''), ('RCONSTAFF', 'moderation.tool'); " +
            "INSERT INTO user_badges (user_id, badge_id, badge_slot) VALUES (962001, 'RCONOWN', 0); " +
            "INSERT INTO rooms (id, caption, owner, model_name) VALUES (962101, 'rcon grant room', 962001, 'model_a')");
    }

    [RconGrantDatabaseFact]
    public async Task AnOfflineBundleAppliesEverythingWithAReceipt()
    {
        var outcome = await Service().GrantBundle(User, "order-1", Payload(new
        {
            credits = 50,
            currencies = new Dictionary<string, int> { ["0"] = 5, ["5"] = 7 },
            furniture = new[] { new { baseId = Chair, amount = 2 } },
            badges = new[] { "RCONT1", "rconown" },
            rank = 3
        }));

        Assert.Equal(GrantOutcome.Ok, outcome.Code);
        var result = (JsonElement)outcome.Result!;
        Assert.Equal(150, result.GetProperty("credits").GetInt32());
        Assert.Equal(15, result.GetProperty("currencies").GetProperty("0").GetInt32());
        Assert.Equal(7, result.GetProperty("currencies").GetProperty("5").GetInt32());
        Assert.Equal(2, result.GetProperty("furniture").GetProperty("items").GetInt32());
        Assert.Equal(["RCONT1"], result.GetProperty("badges").GetProperty("granted").EnumerateArray().Select(code => code.GetString()));
        Assert.Equal(["RCONOWN"], result.GetProperty("badges").GetProperty("skipped").EnumerateArray().Select(code => code.GetString()));
        Assert.Equal("moderator", result.GetProperty("rank").GetProperty("role").GetString());
        Assert.True(result.GetProperty("rank").GetProperty("assigned").GetBoolean());

        Assert.Equal(150, Scalar("SELECT credits FROM users WHERE id = 962001"));
        Assert.Equal(15, Scalar("SELECT amount FROM user_currencies WHERE user_id = 962001 AND type = 0"));
        Assert.Equal(7, Scalar("SELECT amount FROM user_currencies WHERE user_id = 962001 AND type = 5"));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 962001 AND base_item = 962201 AND room_id = 0"));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM user_badges WHERE user_id = 962001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = 962001 AND r.slug = 'moderator' AND ur.expires_at IS NULL"));
        Assert.Equal(3, Scalar("SELECT `rank` FROM users WHERE id = 962001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM acl_audit_log WHERE target_id = 962001 AND action = 'role.assign'"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM rcon_grants WHERE idempotency_key = 'order-1' AND user_id = 962001 AND status = 'applied'"));
    }

    [RconGrantDatabaseFact]
    public async Task AnOnlineUserIsRefusedWithoutAnyChangeAndTheKeyStaysFree()
    {
        var (_, client) = Online(User, 100);
        var payload = Payload(new { credits = 5 });

        Assert.Equal(GrantOutcome.UserOnline, (await Service().GrantBundle(User, "order-online", payload)).Code);
        Assert.Equal(100, Scalar("SELECT credits FROM users WHERE id = 962001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM rcon_grants WHERE idempotency_key = 'order-online'"));

        _clients.UnregisterClient(client, User, "rcon_grant_user");
        Assert.Equal(GrantOutcome.Ok, (await Service().GrantBundle(User, "order-online", payload)).Code);
        Assert.Equal(105, Scalar("SELECT credits FROM users WHERE id = 962001"));
    }

    [RconGrantDatabaseFact]
    public async Task ABalanceBelowZeroIsRefusedWithoutAnyChange()
    {
        var service = Service();

        Assert.Equal(GrantOutcome.InsufficientBalance, (await service.GrantBundle(User, "order-neg", Payload(new { credits = -101, badges = new[] { "RCONT1" } }))).Code);
        Assert.Equal(GrantOutcome.InsufficientBalance, (await service.GrantBundle(User, "order-neg", Payload(new { credits = 5, currencies = new Dictionary<string, int> { ["0"] = -11 } }))).Code);
        Assert.Equal(GrantOutcome.BalanceOverflow, (await service.GrantBundle(User, "order-neg", Payload(new { credits = int.MaxValue }))).Code);
        AssertUntouched();
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM rcon_grants WHERE idempotency_key = 'order-neg'"));

        Assert.Equal(GrantOutcome.Ok, (await service.GrantBundle(User, "order-neg", Payload(new { credits = -100 }))).Code);
        Assert.Equal(0, Scalar("SELECT credits FROM users WHERE id = 962001"));
    }

    [RconGrantDatabaseFact]
    public async Task ABadItemOrBadgeRefusesTheWholeBundle()
    {
        var service = Service();
        var furniture = new[] { new { baseId = Chair, amount = 1 }, new { baseId = 962999u, amount = 1 } };

        Assert.Equal(GrantOutcome.UnknownFurniture, (await service.GrantBundle(User, "order-bad", Payload(new { credits = 5, furniture }))).Code);
        Assert.Equal(GrantOutcome.FurnitureNotGrantable, (await service.GrantBundle(User, "order-bad", Payload(new { credits = 5, furniture = new[] { new { baseId = Locked, amount = 1 } } }))).Code);
        Assert.Equal(GrantOutcome.UnknownBadge, (await service.GrantBundle(User, "order-bad", Payload(new { credits = 5, badges = new[] { "RCONT1", "RCONNONE" } }))).Code);
        Assert.Equal(GrantOutcome.RestrictedBadge, (await service.GrantBundle(User, "order-bad", Payload(new { credits = 5, badges = new[] { "RCONSTAFF" } }))).Code);
        AssertUntouched();
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM rcon_grants WHERE idempotency_key = 'order-bad'"));

        _rights.Add("moderation.tool");
        Assert.Equal(GrantOutcome.Ok, (await service.GrantBundle(User, "order-bad", Payload(new { badges = new[] { "RCONSTAFF" } }))).Code);
    }

    [RconGrantDatabaseFact]
    public void AFailureAfterTheWritesRollsBackEveryOneOfThem()
    {
        // The receipt insert is the last statement; a key already taken fails it after every other write ran.
        Sql("INSERT INTO rcon_grants (idempotency_key, user_id, payload_sha256, result_json) VALUES ('order-taken', 962002, REPEAT('0', 64), '{}')");
        Assert.Null(GrantBundle.TryParse(Payload(new
        {
            credits = 50,
            currencies = new Dictionary<string, int> { ["0"] = 5 },
            furniture = new[] { new { baseId = Chair, amount = 3 } },
            badges = new[] { "RCONT1" },
            rank = 4
        }), out var bundle));

        Assert.Null(_store.ApplyBundle(User, "order-taken", bundle, _ => false));

        AssertUntouched();
    }

    [RconGrantDatabaseFact]
    public async Task AReplayReturnsTheStoredResultAndADifferentPayloadOrUserIsRejected()
    {
        var service = Service();
        var payload = Payload(new { credits = 10, furniture = new[] { new { baseId = Chair, amount = 1 } } });

        var first = await service.GrantBundle(User, "order-replay", payload);
        var replay = await service.GrantBundle(User, "order-replay", payload);

        Assert.Equal(GrantOutcome.Ok, first.Code);
        Assert.Equal(GrantOutcome.AlreadyApplied, replay.Code);
        Assert.Equal(JsonSerializer.Serialize(first.Result), JsonSerializer.Serialize(replay.Result));
        Assert.Equal(110, Scalar("SELECT credits FROM users WHERE id = 962001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 962001"));

        Assert.Equal(GrantOutcome.KeyPayloadMismatch, (await service.GrantBundle(User, "order-replay", Payload(new { credits = 11 }))).Code);
        Assert.Equal(GrantOutcome.KeyPayloadMismatch, (await service.GrantBundle(Other, "order-replay", payload)).Code);
        Assert.Equal(110, Scalar("SELECT credits FROM users WHERE id = 962001"));
        Assert.Equal(100, Scalar("SELECT credits FROM users WHERE id = 962002"));
    }

    [RconGrantDatabaseFact]
    public async Task AReplayWinsOverTheOnlineCheck()
    {
        var payload = Payload(new { credits = 10 });
        Assert.Equal(GrantOutcome.Ok, (await Service().GrantBundle(User, "order-late", payload)).Code);
        Online(User, 110);

        Assert.Equal(GrantOutcome.AlreadyApplied, (await Service().GrantBundle(User, "order-late", payload)).Code);
    }

    [RconGrantDatabaseFact]
    public async Task ARetryWaitingOnTheGateReplaysAReceiptCommittedMeanwhileEvenAfterALoginAndADefinitionChange()
    {
        var payload = Payload(new { credits = 10, furniture = new[] { new { baseId = Chair, amount = 1 } } });
        var held = await _gate.EnterAsync(User);
        Task<GrantOutcome> retry;

        try {
            // The retry misses the receipt, then waits on the gate the original request (or a login) holds.
            retry = Task.Run(() => Service().GrantBundle(User, "order-retry", payload));
            await Task.Delay(200);
            Assert.False(retry.IsCompleted);
            Assert.Null(GrantBundle.TryParse(payload, out var bundle));
            Assert.Equal(GrantOutcome.Ok, _store.ApplyBundle(User, "order-retry", bundle, _ => false)!.Code);
            Online(User, 110);
            _definitions.Remove(Chair);
        }
        finally {
            held.Dispose();
        }

        Assert.Equal(GrantOutcome.AlreadyApplied, (await retry.WaitAsync(Pause)).Code);
        Assert.Equal(110, Scalar("SELECT credits FROM users WHERE id = 962001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 962001"));
    }

    [RconGrantDatabaseFact]
    public async Task ALoginStartedDuringAGrantWaitsAndLoadsTheGrantedWallet()
    {
        var store = new PausedStore(_store);
        var grant = Task.Run(() => Service(store).GrantBundle(User, "order-race", Payload(new { credits = 50 })));
        Task<int> login;

        try {
            await store.Entered.Task.WaitAsync(Pause);
            login = Task.Run(async () =>
            {
                using (await _gate.EnterAsync(User)) {
                    return Scalar("SELECT credits FROM users WHERE id = 962001");
                }
            });
            await Task.Delay(200);
            Assert.False(login.IsCompleted);
        }
        finally {
            store.Release.TrySetResult();
        }

        Assert.Equal(GrantOutcome.Ok, (await grant.WaitAsync(Pause)).Code);
        Assert.Equal(150, await login.WaitAsync(Pause));
    }

    [RconGrantDatabaseFact]
    public async Task AGrantWaitingOnALoginIsRefusedOnceTheSessionRegisters()
    {
        var held = await _gate.EnterAsync(User);
        Task<GrantOutcome> grant;

        try {
            grant = Task.Run(() => Service().GrantBundle(User, "order-wait", Payload(new { credits = 50 })));
            await Task.Delay(200);
            Assert.False(grant.IsCompleted);
            Online(User, 100);
        }
        finally {
            held.Dispose();
        }

        Assert.Equal(GrantOutcome.UserOnline, (await grant.WaitAsync(Pause)).Code);
        Assert.Equal(100, Scalar("SELECT credits FROM users WHERE id = 962001"));
    }

    [RconGrantDatabaseFact]
    public async Task AReplacedSessionsDelayedLogoutCannotOverwriteAnOfflineGrant()
    {
        var (old, oldClient) = Online(User, 100);

        // A new login replaces the session, then logs out itself before the replaced session's logout ran.
        var replacement = await Login();
        replacement.Habbo.Save();
        _clients.UnregisterClient(replacement.Client, User, "rcon_grant_user");

        Assert.Equal(GrantOutcome.Ok, (await Service().GrantBundle(User, "order-replaced", Payload(new { credits = 50 }))).Code);
        Assert.Equal(150, Scalar("SELECT credits FROM users WHERE id = 962001"));

        // The replaced session's late logout: its save is the step OnDisconnect runs before unregistering.
        old.Save();
        _clients.UnregisterClient(oldClient, User, "rcon_grant_user");

        Assert.Equal(150, Scalar("SELECT credits FROM users WHERE id = 962001"));
    }

    [RconGrantDatabaseFact]
    public async Task AReplacedSessionsDelayedLogoutCannotOverwriteTheNewSessionsChanges()
    {
        var (old, _) = Online(User, 100);
        old.Credits = 90;

        var replacement = await Login();
        Assert.Equal(90, replacement.Habbo.Credits);
        Assert.True((await Maintenance().GiveCurrency(User, "credits", 20)).Succeeded);
        Assert.Equal(110, Scalar("SELECT credits FROM users WHERE id = 962001"));

        old.Save();

        Assert.Equal(110, Scalar("SELECT credits FROM users WHERE id = 962001"));
    }

    [RconGrantDatabaseFact]
    public async Task AnOrdinaryLogoutPausedBeforeItsSaveHoldsOffEveryOfflineWriteUntilItSaved()
    {
        var (habbo, _) = Online(User, 100);
        var persistence = new PausedPersistence(habbo.Persistence);
        habbo.Persistence = persistence;

        // The real logout: OnDisconnect marks the session disconnected, then saves. Unregistering needs the hotel, so it throws here.
        var logout = Task.Run(() =>
        {
            try {
                habbo.OnDisconnect();
            }
            catch (InvalidOperationException) { }
        });
        Task<GrantOutcome> grant, settings;

        try {
            await persistence.Entered.Task.WaitAsync(Pause);
            grant = Task.Run(() => Service().GrantBundle(User, "order-logout", Payload(new { credits = 50 })));
            settings = Task.Run(() => Service().UpdateSettings(User, Payload(new { homeRoom = Room })));
            var give = Task.Run(() => Maintenance().GiveCurrency(Other, "credits", 1));
            await Task.Delay(300);
            Assert.False(grant.IsCompleted);
            Assert.False(settings.IsCompleted);
            Assert.True((await give.WaitAsync(Pause)).Succeeded);
        }
        finally {
            persistence.Release.TrySetResult();
        }

        await logout.WaitAsync(Pause);
        Assert.Equal(GrantOutcome.Ok, (await grant.WaitAsync(Pause)).Code);
        Assert.Contains("\"online\":false", JsonSerializer.Serialize((await settings.WaitAsync(Pause)).Result));
        Assert.Equal(150, Scalar("SELECT credits FROM users WHERE id = 962001"));
        Assert.Equal(Room, Scalar("SELECT home_room FROM users_settings WHERE user_id = 962001"));
    }

    [RconGrantDatabaseFact]
    public async Task OfflineCurrencyChangesAreLockedAndNeverGoBelowZero()
    {
        var maintenance = Maintenance();

        var given = await maintenance.GiveCurrency(User, "credits", 25);
        Assert.Equal(GrantOutcome.Ok, given.Code);
        Assert.Equal(125, Scalar("SELECT credits FROM users WHERE id = 962001"));
        Assert.Contains("\"online\":false", JsonSerializer.Serialize(given.Result));

        Assert.Equal(GrantOutcome.InsufficientBalance, (await maintenance.TakeCurrency(User, "diamonds", 1)).Code);
        Assert.Equal(GrantOutcome.InsufficientBalance, (await maintenance.TakeCurrency(User, "duckets", 11)).Code);
        Assert.Equal(GrantOutcome.Ok, (await maintenance.TakeCurrency(User, "duckets", 10)).Code);
        Assert.Equal(0, Scalar("SELECT amount FROM user_currencies WHERE user_id = 962001 AND type = 0"));
        Assert.Equal(GrantOutcome.UserNotFound, (await maintenance.GiveCurrency(962999, "credits", 1)).Code);
        Assert.Equal(GrantOutcome.InvalidPayload, (await maintenance.GiveCurrency(User, "stars", 1)).Code);
    }

    [RconGrantDatabaseFact]
    public async Task BadgesFollowTheDatabaseDefinitionsAndOnlyLeaveOfflineAccounts()
    {
        var service = Service();
        Sql("INSERT INTO badge_definitions (code, required_right) VALUES ('RCONNEW', '')");

        Assert.Equal(GrantOutcome.Ok, (await service.GiveBadge(User, "RCONNEW")).Code);
        Assert.Contains("\"granted\":false", JsonSerializer.Serialize((await service.GiveBadge(User, "RCONNEW")).Result));
        Assert.Equal(GrantOutcome.UnknownBadge, (await service.GiveBadge(User, "RCONNONE")).Code);
        Assert.Equal(GrantOutcome.RestrictedBadge, (await service.GiveBadge(User, "RCONSTAFF")).Code);
        Assert.Equal(GrantOutcome.UserNotFound, (await service.GiveBadge(962999, "RCONNEW")).Code);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM user_badges WHERE user_id = 962001 AND badge_id = 'RCONNEW'"));

        Assert.Contains("\"removed\":true", JsonSerializer.Serialize((await service.TakeBadge(User, "RCONNEW")).Result));
        Assert.Contains("\"removed\":false", JsonSerializer.Serialize((await service.TakeBadge(User, "RCONNEW")).Result));
        Online(User, 100);
        Assert.Equal(GrantOutcome.UserOnline, (await service.TakeBadge(User, "RCONOWN")).Code);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM user_badges WHERE user_id = 962001 AND badge_id = 'RCONOWN'"));
    }

    [RconGrantDatabaseFact]
    public async Task SettingsAreWrittenOfflineAndToTheLiveSessionItsLogoutSaves()
    {
        var service = Service();

        Assert.Equal(GrantOutcome.UnknownRoom, (await service.UpdateSettings(User, Payload(new { homeRoom = 962999 }))).Code);
        Assert.Equal(GrantOutcome.InvalidPayload, (await service.UpdateSettings(User, Payload(new { motto = "x" }))).Code);
        Assert.Equal(GrantOutcome.Ok, (await service.UpdateSettings(User, Payload(new { homeRoom = Room, friendBarState = 0 }))).Code);
        Assert.Equal(Room, Scalar("SELECT home_room FROM users_settings WHERE user_id = 962001"));
        Assert.Equal(0, Scalar("SELECT friend_bar_state FROM users_settings WHERE user_id = 962001"));

        var (habbo, _) = Online(User, 100);
        Assert.Equal(GrantOutcome.Ok, (await service.UpdateSettings(User, Payload(new { homeRoom = 0 }))).Code);
        Assert.Equal(0u, habbo.HomeRoom);
        habbo.Save();
        Assert.Equal(0, Scalar("SELECT home_room FROM users_settings WHERE user_id = 962001"));
    }

    [RconGrantDatabaseFact]
    public async Task TheFriendBarStateSurvivesEveryLoginAndLogoutRoundTrip()
    {
        foreach (var state in new[] { 0, 1 }) {
            Sql($"UPDATE users_settings SET friend_bar_state = {state} WHERE user_id = 962001");

            (await LoadFromDatabase()).Save();

            Assert.Equal(state, Scalar("SELECT friend_bar_state FROM users_settings WHERE user_id = 962001"));
        }

        // A settings write, then logout, login and logout keep it, whether it was written offline or into the live session.
        Assert.Equal(GrantOutcome.Ok, (await Service().UpdateSettings(User, Payload(new { friendBarState = 1 }))).Code);
        (await LoadFromDatabase()).Save();
        Assert.Equal(1, Scalar("SELECT friend_bar_state FROM users_settings WHERE user_id = 962001"));

        var live = await LoadFromDatabase();
        var (client, _) = HabbiconTestSupport.Client(live);
        _clients.RegisterClient(client, User, "rcon_grant_user");
        Assert.Equal(GrantOutcome.Ok, (await Service().UpdateSettings(User, Payload(new { friendBarState = 0 }))).Code);
        live.Save();
        _clients.UnregisterClient(client, User, "rcon_grant_user");
        (await LoadFromDatabase()).Save();
        Assert.Equal(0, Scalar("SELECT friend_bar_state FROM users_settings WHERE user_id = 962001"));
    }

    public void Dispose() => Clean();

    private UserGrantService Service(IUserGrantStore? store = null) => new(store ?? _store, _gate, _clients, _items, _access);

    private UserMaintenanceService Maintenance() => new(new UserMaintenanceStore(_database), _gate, _clients);

    private (Habbo Habbo, GameClient Client) Online(int userId, int credits)
    {
        var habbo = new Habbo
        {
            Id = userId,
            Username = "rcon_grant_user",
            Credits = credits,
            HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0),
            Persistence = new UserPersistenceService(_database, TimeProvider.System, _clients),
            SessionStartedAt = DateTimeOffset.UtcNow
        };
        habbo.Currencies.Load(LoadCurrencies(userId));
        var (client, _) = HabbiconTestSupport.Client(habbo);
        _clients.RegisterClient(client, userId, habbo.Username);

        return (habbo, client);
    }

    // Loads the account the way a login does and gives it what a session's logout save needs.
    private async Task<Habbo> LoadFromDatabase()
    {
        var factory = new UserDataFactory(null!, _database, [], null!, null!, null!, new Plus.HabboHotel.Rooms.RoomVisitRecorder(_database, TimeProvider.System),
            TimeProvider.System, TestRoomAchievements.Unused, _clients, TestRoomManager.Unused);
        var habbo = (await factory.GetUserDataByIdAsync(User))!;
        habbo.HabboStats ??= new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        habbo.Persistence = new UserPersistenceService(_database, TimeProvider.System, _clients);
        habbo.SessionStartedAt = DateTimeOffset.UtcNow;

        return habbo;
    }

    // The login path inside the gate: the authentication tasks run, then the account is loaded from the database and registered.
    private async Task<(Habbo Habbo, GameClient Client)> Login()
    {
        using (await _gate.EnterAsync(User)) {
            Assert.True(await new DisconnectCurrentOnlineHabboTask(_clients).CanLogin(User));

            return Online(User, Scalar("SELECT credits FROM users WHERE id = 962001"));
        }
    }

    private IReadOnlyList<KeyValuePair<int, int>> LoadCurrencies(int userId)
    {
        using var connection = _database.Connection();

        return UserCurrencyStore.Load(connection, userId);
    }

    private void AssertUntouched()
    {
        Assert.Equal(100, Scalar("SELECT credits FROM users WHERE id = 962001"));
        Assert.Equal(10, Scalar("SELECT SUM(amount) FROM user_currencies WHERE user_id = 962001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM items WHERE user_id = 962001"));
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM user_badges WHERE user_id = 962001"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM user_roles WHERE user_id = 962001"));
        Assert.Equal(1, Scalar("SELECT `rank` FROM users WHERE id = 962001"));
    }

    private static string Payload(object payload) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

    private void Clean() => Sql("DELETE FROM rcon_grants WHERE user_id IN (962001, 962002) OR idempotency_key LIKE 'order-%'; " +
        "DELETE FROM items WHERE user_id IN (962001, 962002); DELETE FROM user_badges WHERE user_id IN (962001, 962002); " +
        "DELETE FROM user_roles WHERE user_id IN (962001, 962002); DELETE FROM acl_audit_log WHERE target_id IN (962001, 962002); " +
        "DELETE FROM user_currencies WHERE user_id IN (962001, 962002); DELETE FROM users_settings WHERE user_id IN (962001, 962002); " +
        "DELETE FROM user_statistics WHERE id IN (962001, 962002); DELETE FROM rooms WHERE id = 962101; " +
        "DELETE FROM badge_definitions WHERE code LIKE 'RCON%'; DELETE FROM users WHERE id IN (962001, 962002)");

    private void Sql(string sql)
    {
        using var connection = _database.Connection();
        connection.Execute(sql);
    }

    private int Scalar(string sql)
    {
        using var connection = _database.Connection();

        return connection.QuerySingle<int>(sql);
    }

    // Holds a logout inside its save until released.
    private sealed class PausedPersistence(IUserPersistenceService inner) : IUserPersistenceService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Save(Habbo habbo)
        {
            Entered.TrySetResult();

            if (!Release.Task.Wait(Pause)) {
                throw new TimeoutException("The paused test step was never released.");
            }

            inner.Save(habbo);
        }
        public void MarkOnline(GameClient session, int userId) => inner.MarkOnline(session, userId);
        public void SetProfileValue(int userId, string column, object? value) => inner.SetProfileValue(userId, column, value);
    }

    // Holds a grant inside its gate until released, so a login can be started against it.
    private sealed class PausedStore(IUserGrantStore inner) : IUserGrantStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GrantReceipt? FindReceipt(string key) => inner.FindReceipt(key);
        public GrantOutcome? ApplyBundle(int userId, string key, GrantBundle bundle, Func<string, bool> hasRight)
        {
            Entered.SetResult();

            if (!Release.Task.Wait(Pause)) {
                throw new TimeoutException("The paused test step was never released.");
            }

            return inner.ApplyBundle(userId, key, bundle, hasRight);
        }
        public BadgeDefinitionRow? FindBadge(string code) => inner.FindBadge(code);
        public bool InsertBadge(int userId, string code) => inner.InsertBadge(userId, code);
        public bool UserExists(int userId) => inner.UserExists(userId);
        public bool DeleteBadge(int userId, string code) => inner.DeleteBadge(userId, code);
        public (string? Error, uint HomeRoom, int FriendBarState) UpdateSettings(int userId, uint? homeRoom, int? friendBarState) =>
            inner.UpdateSettings(userId, homeRoom, friendBarState);
    }
}
