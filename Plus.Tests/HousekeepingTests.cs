using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Housekeeping;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Attributes;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests;

public class HousekeepingPolicyTests
{
    [Theory]
    [InlineData(70, 60, true)]
    [InlineData(70, 70, false)]
    [InlineData(70, 80, false)]
    [InlineData(0, 0, false)]
    public void StaffOnlyTargetStrictlyLowerRoleWeights(int actorWeight, int targetWeight, bool allowed) =>
        Assert.Equal(allowed, Access(actorWeight).Outranks(Access(targetWeight)));

    internal static UserAccess Access(int weight, params string[] rights) => UserAccess.Create(
        [new(new AccessRole(weight, "test", "Test", weight, Math.Min(weight / 10, 7), "", false, rights, new Dictionary<string, int>()))]);

    [Fact]
    public void BalancesRejectGrantsThatOverflowTheWireInt()
    {
        Assert.Equal(150, HousekeepingLimits.AddToBalance(100, 50));
        Assert.Equal(int.MaxValue, HousekeepingLimits.AddToBalance(int.MaxValue - 1, 1));
        Assert.Null(HousekeepingLimits.AddToBalance(int.MaxValue, 1));
        Assert.Null(HousekeepingLimits.AddToBalance(-5, 1));
    }

    [Fact]
    public void DurationsSaturateAtTheIntRange()
    {
        Assert.Equal(1_000 + 3_600, HousekeepingLimits.UnixUntil(1_000, 3_600));
        Assert.Equal(int.MaxValue, HousekeepingLimits.UnixUntil(int.MaxValue - 10, (long)HousekeepingLimits.MaxBanHours * 3600));
        Assert.Equal(1_000, HousekeepingLimits.UnixUntil(1_000, -50));
    }

    [Fact]
    public void AuditValuesAreSingleLineAndBounded()
    {
        Assert.Equal("a b c d", HousekeepingLimits.AuditValue("  a\rb\nc\td  "));
        Assert.Equal(HousekeepingLimits.MaxReasonLength, HousekeepingLimits.AuditValue(new string('x', 2_000)).Length);
        Assert.Equal(string.Empty, HousekeepingLimits.AuditValue(null!));
    }

    [Fact]
    public void LikeSearchesEscapeWildcards() =>
        Assert.Equal("100\\%\\_off\\\\", HousekeepingRoomStore.EscapeLike("100%_off\\"));

    [Fact]
    public void ResetPasswordsAreLongUnambiguousAndRandom()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ => HousekeepingUserActions.GeneratePassword()).ToList();
        Assert.All(passwords, password =>
        {
            Assert.True(password.Length >= 12);
            Assert.DoesNotContain(password, c => "0O1lI".Contains(c) || !char.IsLetterOrDigit(c));
        });
        Assert.Equal(passwords.Count, passwords.Distinct().Count());
    }
}

public class HousekeepingActionTests
{
    private static Habbo Staff(int rank = 7, params string[] rights) =>
        new() { Id = 1, Username = "staff", Access = HousekeepingPolicyTests.Access(rank * 10, rights) };

    private static (HousekeepingUserActions Users, HousekeepingEconomyActions Economy, FakeClients Clients) Actions(params (HousekeepingUserRecord User, UserAccess Access)[] users) =>
        Actions(new AccountSessionGate(), null!, null!, users);

    private static (HousekeepingUserActions Users, HousekeepingEconomyActions Economy, FakeClients Clients) Actions(IAccountSessionGate gate,
        IItemDataManager items, IItemFactory itemFactory, params (HousekeepingUserRecord User, UserAccess Access)[] users)
    {
        var store = new FakeUserStore(users.Select(user => user.User));
        var permissions = DispatchProxy.Create<IAccessControl, AccessProxy>();
        ((AccessProxy)(object)permissions).Users = users.ToDictionary(user => user.User.Id, user => user.Access);
        ((AccessProxy)(object)permissions).Users.TryAdd(1, Staff().Access);
        var clients = new FakeClients();

        return (new(store, clients, null!, permissions, null!, null!, gate, null!, null!, TimeProvider.System), new(store, clients, items, itemFactory, null!, null!, gate, permissions), clients);
    }

    [Fact]
    public async Task GrantsWaitForALoginInProgressAndThenUseTheRegisteredWallet()
    {
        var gate = new AccountSessionGate();
        var (_, economy, clients) = Actions(gate, null!, null!, User(2, 1));
        var login = await gate.EnterAsync(2);
        var grant = Task.Run(() => economy.Give(Staff(), 2, HousekeepingCurrency.Credits, 50));
        await Task.Delay(200);
        Assert.False(grant.IsCompleted);

        // The login finishes loading (balance 100) and registers before it leaves the gate.
        var target = new Habbo { Id = 2, Access = HousekeepingPolicyTests.Access(10), Credits = 100 };
        clients.Online[2] = Client(target).Client;
        login.Dispose();

        Assert.True((await grant).Ok);
        Assert.Equal(150, target.Credits);
    }

    [Fact]
    public void GrantedFurnitureRefreshesTheInventoryOncePerBatch()
    {
        var definition = new ItemDefinition { Id = 1500, ItemName = "chair", Type = Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Floor, InteractionType = InteractionType.None };
        var items = DispatchProxy.Create<IItemDataManager, ItemsProxy>();
        ((ItemsProxy)(object)items).Items[1500] = definition;
        var factory = DispatchProxy.Create<IItemFactory, FactoryProxy>();
        var (_, economy, clients) = Actions(new AccountSessionGate(), items, factory, User(2, 1));
        var target = new Habbo { Id = 2, Access = HousekeepingPolicyTests.Access(10), Inventory = new() { Furniture = new([], []) } };
        var (client, sent) = Client(target);
        clients.Online[2] = client;

        Assert.True(economy.GrantItem(Staff(), 2, 1500, 3).Ok);
        Assert.Equal(new[] { ServerPacketHeader.FurniListNotificationComposer, ServerPacketHeader.FurniListNotificationComposer,
            ServerPacketHeader.FurniListNotificationComposer, ServerPacketHeader.FurniListUpdateComposer }, sent.Select(packet => packet.Header));
    }

    public class ItemsProxy : DispatchProxy
    {
        public Dictionary<uint, ItemDefinition> Items { get; } = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == "get_Items" ? Items : throw new NotSupportedException(method?.Name);
    }

    public class FactoryProxy : DispatchProxy
    {
        private uint _nextId = 10;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IItemFactory.CreateMultipleItems) && args![1] is int ownerId
                ? Enumerable.Range(0, (int)args[3]!).Select(_ => new Item { Id = _nextId++, OwnerId = (uint)ownerId, Definition = (ItemDefinition)args[0]! }).ToList()
                : throw new NotSupportedException(method?.Name);
    }

    private static (HousekeepingUserRecord User, UserAccess Access) User(int id, int rank) =>
        (new() { Id = id, Username = "user" + id }, HousekeepingPolicyTests.Access(rank * 10));

    public class AccessProxy : DispatchProxy
    {
        public Dictionary<int, UserAccess> Users { get; set; } = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IAccessControl.Resolve) => Users.GetValueOrDefault((int)args![0]!, UserAccess.Empty),
            nameof(IAccessControl.Outranks) => (int)args![0]! != (int)args[1]! &&
                Users.GetValueOrDefault((int)args[0]!, UserAccess.Empty).Outranks(Users.GetValueOrDefault((int)args[1]!, UserAccess.Empty)),
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(HousekeepingLimits.MaxBanHours + 1)]
    public void BanHoursAreBounded(int hours) =>
        Assert.Equal(HousekeepingErrors.InvalidInput, Actions(User(2, 1)).Users.Ban(Staff(), 2, "spam", hours).Result.Message);

    [Fact]
    public void LongReasonsAreRejected() =>
        Assert.Equal(HousekeepingErrors.InvalidInput, Actions(User(2, 1)).Users.Mute(Staff(), 2, new string('x', HousekeepingLimits.MaxReasonLength + 1), 5).Message);

    [Theory]
    [InlineData(0)]
    [InlineData(HousekeepingLimits.MaxMuteMinutes + 1)]
    public void MuteMinutesAreBounded(int minutes) =>
        Assert.Equal(HousekeepingErrors.InvalidInput, Actions(User(2, 1)).Users.Mute(Staff(), 2, "", minutes).Message);

    [Theory]
    [InlineData(0)]
    [InlineData(HousekeepingLimits.MaxTradeLockHours + 1)]
    public void TradeLockHoursAreBounded(int hours) =>
        Assert.Equal(HousekeepingErrors.InvalidInput, Actions(User(2, 1)).Users.TradeLock(Staff(), 2, hours, "").Message);

    [Fact]
    public async Task EqualAndHigherRanksCannotBeSanctioned()
    {
        var (users, _, _) = Actions(User(2, 7), User(3, 9));
        Assert.Equal(HousekeepingErrors.RankTooHigh, (await users.Ban(Staff(), 2, "", 1)).Message);
        Assert.Equal(HousekeepingErrors.RankTooHigh, users.Kick(Staff(), 3, "").Message);
        Assert.Equal(HousekeepingErrors.RankTooHigh, users.ResetPassword(Staff(), 3).Message);
        Assert.Equal(HousekeepingErrors.UserNotFound, users.Unban(Staff(), 4).Message);
    }

    [Fact]
    public void StaffCannotGrantThemselvesCurrency()
    {
        var (_, economy, _) = Actions(User(1, 7));
        Assert.Equal(HousekeepingErrors.RankTooHigh, economy.Give(Staff(), 1, HousekeepingCurrency.Credits, 100).Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(HousekeepingLimits.MaxGrantAmount + 1)]
    public void GrantAmountsAreBounded(int amount) =>
        Assert.Equal(HousekeepingErrors.InvalidInput, Actions(User(2, 1)).Economy.Give(Staff(), 2, HousekeepingCurrency.Diamonds, amount).Message);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 0)]
    [InlineData(5, HousekeepingLimits.MaxItemQuantity + 1)]
    public void ItemGrantsNeedAnItemAndABoundedQuantity(int itemId, int quantity) =>
        Assert.Equal(HousekeepingErrors.InvalidInput, Actions(User(2, 1)).Economy.GrantItem(Staff(), 2, itemId, quantity).Message);

    [Theory]
    [InlineData(-1)]
    [InlineData(HousekeepingLimits.MaxClubDays + 1)]
    public void ClubDaysAreBounded(int days) =>
        Assert.Equal(HousekeepingErrors.InvalidInput, Actions(User(2, 1)).Economy.SetClub(Staff(), 2, days).Message);

    [Fact]
    public void OnlineCurrencyGrantsUpdateTheLiveWalletAndRejectOverflow()
    {
        var (_, economy, clients) = Actions(User(2, 1));
        var target = new Habbo { Id = 2, Access = HousekeepingPolicyTests.Access(10), Credits = 100 };
        var (client, sent) = Client(target);
        clients.Online[2] = client;

        var granted = economy.Give(Staff(), 2, HousekeepingCurrency.Credits, 50);
        Assert.True(granted.Ok);
        Assert.Equal(2, granted.ActionId);
        Assert.Equal(150, target.Credits);
        Assert.Equal(ServerPacketHeader.CreditBalanceComposer, Assert.Single(sent).Header);

        target.Diamonds = int.MaxValue - 1;
        Assert.Equal(HousekeepingErrors.BalanceOverflow, economy.Give(Staff(), 2, HousekeepingCurrency.Diamonds, 2).Message);
        Assert.Equal(int.MaxValue - 1, target.Diamonds);
    }

    internal sealed class FakeUserStore : IHousekeepingUserStore
    {
        private readonly Dictionary<int, HousekeepingUserRecord> _users;
        public FakeUserStore(IEnumerable<HousekeepingUserRecord> users) => _users = users.ToDictionary(user => user.Id);
        public HousekeepingUserRecord? Find(int userId) => _users.GetValueOrDefault(userId);
        public HousekeepingUserRecord? Find(string username) => _users.Values.FirstOrDefault(user => user.Username == username);
    }

    internal sealed class FakeClients : IGameClientManager
    {
        public Dictionary<int, GameClient> Online { get; } = new();
        public List<IServerPacket> Broadcasts { get; } = new();
        public int Count => Online.Count;
        public ICollection<GameClient> GetClients => Online.Values;
        public GameClient? GetClientByUserId(int userId) => Online.GetValueOrDefault(userId);
        public GameClient? GetClientByUsername(string username) => Online.Values.FirstOrDefault(client => client.GetHabbo().Username == username);
        public void SendPacket(IServerPacket packet, PermissionDefinition? permission = null) => Broadcasts.Add(packet);
        public void OnCycle() { }
        public bool TryGetClient(Guid clientId, out GameClient client) => throw new NotSupportedException();
        public bool TryChangeClientUsername(GameClient client, string oldUsername, string newUsername, Func<bool> persist) => throw new NotSupportedException();
        public Task<string> GetNameById(int id) => throw new NotSupportedException();
        public IEnumerable<GameClient> GetClientsById(Dictionary<int, MessengerBuddy>.KeyCollection users) => throw new NotSupportedException();
        public void StaffAlert(IServerPacket message, int exclude = 0) => throw new NotSupportedException();
        public void ModAlert(string message) => throw new NotSupportedException();
        public void DoAdvertisingReport(GameClient reporter, GameClient target) => throw new NotSupportedException();
        public void LogClonesOut(int userId) => throw new NotSupportedException();
        public void RegisterClient(GameClient client, int userId, string username) => Online[userId] = client;
        public void UnregisterClient(GameClient? client, int userId, string username) => Online.Remove(userId);
        public void CloseAll() => throw new NotSupportedException();
    }
}

public class AccountSessionGateTests
{
    [Fact]
    public async Task AnAccountIsHeldUntilReleasedWhileOthersProceed()
    {
        var gate = new AccountSessionGate();
        var held = gate.Enter(5);
        var same = gate.EnterAsync(5);
        await Task.Delay(100);
        Assert.False(same.IsCompleted);
        held.Dispose();
        (await same).Dispose();
    }

    [Fact]
    public void WaitingIsBounded()
    {
        var gate = new AccountSessionGate(TimeSpan.FromMilliseconds(50));
        using var held = gate.Enter(5);
        Assert.Throws<TimeoutException>(() => gate.Enter(5));
    }

    [Fact]
    public void RevocationStopsOnlyLoginsThatStartedEarlier()
    {
        var gate = new AccountSessionGate();
        var started = gate.Begin();
        gate.Revoke(5);
        Assert.True(gate.IsRevoked(5, started));
        Assert.False(gate.IsRevoked(6, started));
        Assert.False(gate.IsRevoked(5, gate.Begin()));
    }
}

public class HousekeepingHandlerTests
{
    private static Habbo Staff(params string[] rights) =>
        new() { Id = 1, Username = "staff", Access = HousekeepingPolicyTests.Access(70, rights) };

    private static (string Key, bool Ok, int ActionId, string Message) Result(byte[] payload)
    {
        var packet = new FlashIncomingPacket { Buffer = payload };

        return (packet.ReadString(), packet.ReadBool(), packet.ReadInt(), packet.ReadString());
    }

    [Fact]
    public async Task SessionsWithoutPanelAccessGetNoReplyAndNoAudit()
    {
        var audit = new FakeAudit();
        var actions = new FakeActions();
        var (client, sent) = Client(Staff(HousekeepingRights.Sanction));
        await new HousekeepingBanUserEvent(new HousekeepingActionRunner(audit, NullLogger<HousekeepingActionRunner>.Instance), actions)
            .Parse(client, Incoming(2, "spam", 24));
        Assert.Empty(sent);
        Assert.Empty(audit.Rows);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public async Task MissingActionRightIsRefusedAuditedAndAcknowledgedWithTheExactKey()
    {
        var audit = new FakeAudit();
        var actions = new FakeActions();
        var (client, sent) = Client(Staff(HousekeepingRights.Access));
        await new HousekeepingRoomStateEvent(new HousekeepingActionRunner(audit, NullLogger<HousekeepingActionRunner>.Instance), actions)
            .Parse(client, Incoming(5, false));
        Assert.Empty(actions.Calls);
        Assert.Equal(ServerPacketHeader.HousekeepingActionResultComposer, Assert.Single(sent).Header);
        Assert.Equal(("room.close", false, 0, HousekeepingErrors.Forbidden), Result(sent[0].Payload));
        var row = Assert.Single(audit.Rows);
        Assert.Equal("room.close", row.Action);
        Assert.False(row.Outcome.Ok);
    }

    [Fact]
    public async Task OneTimePasswordReachesOnlyTheOperatorNotTheAuditLog()
    {
        var audit = new FakeAudit();
        var actions = new FakeActions { Password = "Secret2345678abc" };
        var (client, sent) = Client(Staff(HousekeepingRights.Access, HousekeepingRights.Password));
        await new HousekeepingResetUserPasswordEvent(new HousekeepingActionRunner(audit, NullLogger<HousekeepingActionRunner>.Instance), actions)
            .Parse(client, Incoming(9));
        Assert.Equal(("user.reset_password", true, 7, "Secret2345678abc"), Result(Assert.Single(sent).Payload));
        var row = Assert.Single(audit.Rows);
        Assert.DoesNotContain("Secret2345678abc", row.Outcome.Detail);
        Assert.Equal("user.reset_password", row.Action);
    }

    [Fact]
    public async Task ActionFailuresAreAuditedAndReportedWithoutLeakingExceptions()
    {
        var audit = new FakeAudit();
        var actions = new FakeActions { Throw = true };
        var (client, sent) = Client(Staff(HousekeepingRights.Access, HousekeepingRights.Economy));
        await new HousekeepingGiveCreditsEvent(new HousekeepingActionRunner(audit, NullLogger<HousekeepingActionRunner>.Instance), actions)
            .Parse(client, Incoming(2, 10));
        Assert.Equal(("user.give_credits", false, 0, "housekeeping.error.db_failed"), Result(Assert.Single(sent).Payload));
        Assert.False(Assert.Single(audit.Rows).Outcome.Ok);
    }

    [Fact]
    public async Task UnsupportedCurrencyTypesAreRejectedUnderTheirOwnKey()
    {
        var actions = new FakeActions();
        var (client, sent) = Client(Staff(HousekeepingRights.Access, HousekeepingRights.Economy));
        await new HousekeepingGiveCurrencyEvent(new HousekeepingActionRunner(new FakeAudit(), NullLogger<HousekeepingActionRunner>.Instance), actions)
            .Parse(client, Incoming(2, -1, 10));
        Assert.Empty(actions.Calls);
        Assert.Equal(("user.give_currency_-1", false, 0, HousekeepingErrors.InvalidInput), Result(Assert.Single(sent).Payload));
    }

    // Arguments are listed in the order Octane's composers write them.
    public static TheoryData<Type, object[], string, string, string> Mutations => new()
    {
        { typeof(HousekeepingBanUserEvent), new object[] { 2, "spam", 24 }, HousekeepingRights.Sanction, "user.ban", "Ban 2 spam 24" },
        { typeof(HousekeepingUnbanUserEvent), new object[] { 2 }, HousekeepingRights.Sanction, "user.unban", "Unban 2" },
        { typeof(HousekeepingMuteUserEvent), new object[] { 2, "flood", 15 }, HousekeepingRights.Sanction, "user.mute", "Mute 2 flood 15" },
        { typeof(HousekeepingKickUserEvent), new object[] { 2, "bye" }, HousekeepingRights.Sanction, "user.kick", "Kick 2 bye" },
        { typeof(HousekeepingForceDisconnectUserEvent), new object[] { 2, "bye" }, HousekeepingRights.Sanction, "user.disconnect", "Disconnect 2 bye" },
        { typeof(HousekeepingTradeLockUserEvent), new object[] { 2, 48, "scam" }, HousekeepingRights.Sanction, "user.trade_lock", "TradeLock 2 48 scam" },
        { typeof(HousekeepingResetUserPasswordEvent), new object[] { 2 }, HousekeepingRights.Password, "user.reset_password", "ResetPassword 2" },
        { typeof(HousekeepingRoomStateEvent), new object[] { 5, true }, HousekeepingRights.Rooms, "room.open", "SetState 5 True" },
        { typeof(HousekeepingMuteRoomEvent), new object[] { 5, 30 }, HousekeepingRights.Rooms, "room.mute", "Mute 5 30" },
        { typeof(HousekeepingKickAllFromRoomEvent), new object[] { 5 }, HousekeepingRights.Rooms, "room.kick_all", "KickAll 5" },
        { typeof(HousekeepingTransferRoomOwnershipEvent), new object[] { 5, 2 }, HousekeepingRights.RoomOwnership, "room.transfer", "TransferOwnership 5 2" },
        { typeof(HousekeepingDeleteRoomEvent), new object[] { 5 }, HousekeepingRights.RoomOwnership, "room.delete", "Delete 5" },
        { typeof(HousekeepingGiveCreditsEvent), new object[] { 2, 100 }, HousekeepingRights.Economy, "user.give_credits", "Give 2 Credits 100" },
        { typeof(HousekeepingGiveCurrencyEvent), new object[] { 2, 5, 100 }, HousekeepingRights.Economy, "user.give_currency_5", "Give 2 Diamonds 100" },
        { typeof(HousekeepingGrantItemEvent), new object[] { 2, 1500, 3 }, HousekeepingRights.Economy, "user.grant_item", "GrantItem 2 1500 3" },
        { typeof(HousekeepingSetHcSubscriptionEvent), new object[] { 2, 31 }, HousekeepingRights.Economy, "user.set_hc", "SetClub 2 31" }
    };

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task MutationsReadTheRendererFieldOrderAndEchoTheActionKey(Type handler, object[] fields, string right, string key, string call)
    {
        var audit = new FakeAudit();
        var actions = new FakeActions();
        var (client, sent) = Client(Staff(HousekeepingRights.Access, right));
        var runner = new HousekeepingActionRunner(audit, NullLogger<HousekeepingActionRunner>.Instance);
        var instance = (IPacketEvent)Activator.CreateInstance(handler, runner, actions)!;
        await instance.Parse(client, Incoming(fields));
        Assert.Equal(call, Assert.Single(actions.Calls));
        Assert.Equal((key, true, 7, ""), Result(Assert.Single(sent).Payload));
        Assert.Equal(key, Assert.Single(audit.Rows).Action);
    }

    [Fact]
    public async Task HotelAlertsBroadcastSignedAndBoundedMessages()
    {
        var clients = new HousekeepingActionTests.FakeClients();
        var audit = new FakeAudit();
        var (client, sent) = Client(Staff(HousekeepingRights.Access, HousekeepingRights.Alert));
        var handler = new HousekeepingSendHotelAlertEvent(new HousekeepingActionRunner(audit, NullLogger<HousekeepingActionRunner>.Instance), clients);
        await handler.Parse(client, Incoming("  Maintenance soon  "));
        var packet = new RecordingPacket();
        Assert.Single(clients.Broadcasts).Compose(packet);
        Assert.Equal("Maintenance soon\r\n- staff", packet.Writes[0]);
        Assert.Equal(("hotel.alert", true, 0, ""), Result(sent[0].Payload));

        await handler.Parse(client, Incoming(new string('x', HousekeepingLimits.MaxAlertLength + 1)));
        await handler.Parse(client, Incoming("   "));
        Assert.Single(clients.Broadcasts);
        Assert.Equal(HousekeepingErrors.InvalidInput, Result(sent[1].Payload).Message);
        Assert.Equal(HousekeepingErrors.AlertEmpty, Result(sent[2].Payload).Message);
    }

    [Fact]
    public void LookupsNeedPanelAccess()
    {
        Assert.Equal(HousekeepingRights.Access, typeof(HousekeepingListActionLogEvent).GetCustomAttribute<RequiresPermissionAttribute>()!.Permissions.Single());
        Assert.Equal(HousekeepingRights.Access, typeof(HousekeepingFindUserByIdEvent).GetCustomAttribute<RequiresPermissionAttribute>()!.Permissions.Single());
    }

    [Fact]
    public async Task ActionLogIsReadBackNewestFirstForStaff()
    {
        var audit = new FakeAudit();
        audit.Entries.Add(new() { Id = 4, CreatedAt = DateTimeOffset.FromUnixTimeSeconds(100), ActorId = 1, ActorName = "staff", TargetType = "room", TargetId = 5, TargetLabel = "Lobby", Action = "room.close", Detail = "open=False", Success = true });
        var (client, sent) = Client(Staff(HousekeepingRights.Access));
        await new HousekeepingListActionLogEvent(new HousekeepingActionRunner(audit, NullLogger<HousekeepingActionRunner>.Instance), audit).Parse(client, Incoming(10_000));
        var packet = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(4, packet.ReadInt());
    }

    private sealed class FakeAudit : IHousekeepingAuditLog
    {
        public List<(string Action, HousekeepingOutcome Outcome)> Rows { get; } = new();
        public List<HousekeepingAuditEntry> Entries { get; } = new();
        public void Write(int actorId, string actorName, string action, HousekeepingOutcome outcome) => Rows.Add((action, outcome));
        public IReadOnlyList<HousekeepingAuditEntry> List(int limit) => Entries;
    }

    private sealed class FakeActions : IHousekeepingUserActions, IHousekeepingRoomActions, IHousekeepingEconomyActions
    {
        public List<string> Calls { get; } = new();
        public string Password { get; init; } = "";
        public bool Throw { get; init; }

        private HousekeepingOutcome Record(string call, string message = "")
        {
            if (Throw) {
                throw new InvalidOperationException("boom");
            }

            Calls.Add(call);

            return HousekeepingOutcome.Success(HousekeepingTarget.User(7, "target"), "done", message);
        }

        public Task<HousekeepingOutcome> Ban(Habbo actor, int userId, string reason, int hours) => Task.FromResult(Record($"Ban {userId} {reason} {hours}"));
        public HousekeepingOutcome Unban(Habbo actor, int userId) => Record($"Unban {userId}");
        public HousekeepingOutcome Mute(Habbo actor, int userId, string reason, int minutes) => Record($"Mute {userId} {reason} {minutes}");
        public HousekeepingOutcome Kick(Habbo actor, int userId, string reason) => Record($"Kick {userId} {reason}");
        public HousekeepingOutcome Disconnect(Habbo actor, int userId, string reason) => Record($"Disconnect {userId} {reason}");
        public HousekeepingOutcome TradeLock(Habbo actor, int userId, int hours, string reason) => Record($"TradeLock {userId} {hours} {reason}");
        public HousekeepingOutcome ResetPassword(Habbo actor, int userId) => Record($"ResetPassword {userId}", Password);
        public HousekeepingOutcome SetState(Habbo actor, int roomId, bool open) => Record($"SetState {roomId} {open}");
        public HousekeepingOutcome Mute(Habbo actor, int roomId, int minutes) => Record($"Mute {roomId} {minutes}");
        public HousekeepingOutcome KickAll(Habbo actor, int roomId) => Record($"KickAll {roomId}");
        public HousekeepingOutcome TransferOwnership(Habbo actor, int roomId, int newOwnerId) => Record($"TransferOwnership {roomId} {newOwnerId}");
        public HousekeepingOutcome Delete(Habbo actor, int roomId) => Record($"Delete {roomId}");
        public HousekeepingOutcome Give(Habbo actor, int userId, HousekeepingCurrency currency, int amount) => Record($"Give {userId} {currency} {amount}");
        public HousekeepingOutcome GrantItem(Habbo actor, int userId, int itemId, int quantity) => Record($"GrantItem {userId} {itemId} {quantity}");
        public HousekeepingOutcome SetClub(Habbo actor, int userId, int days) => Record($"SetClub {userId} {days}");
    }
}

public class HousekeepingWireTests
{
    private static List<object> Writes(IServerPacket composer)
    {
        var packet = new RecordingPacket();
        composer.Compose(packet);

        return packet.Writes;
    }

    private static readonly HousekeepingRoom Lobby = new(5, "Lobby", "Welcome", 2, "owner", 3, 25, true, false, true, 0);

    [Fact]
    public void ActionResultMatchesHousekeepingActionResultParser() =>
        Assert.Equal(new object[] { "user.ban", true, 2, "" }, Writes(new HousekeepingActionResultComposer("user.ban", true, 2, "")));

    [Fact]
    public void UserDetailMatchesHousekeepingUserDetailData()
    {
        Assert.Equal(new object[] { false }, Writes(new HousekeepingUserDetailComposer(null)));
        var user = new HousekeepingUserDetail(2, "Alice", "hi", "hd-180-1", 3, "Moderator", true, 1_700_000_000, 10, 20, 30, "a@b.c", "1.2.3.4", false, true, false);
        Assert.Equal(new object[] { true, 2, "Alice", "hi", "hd-180-1", 3, "Moderator", true, 1_700_000_000, 10, 20, 30, "a@b.c", "1.2.3.4", false, true, false },
            Writes(new HousekeepingUserDetailComposer(user)));
    }

    [Fact]
    public void RoomDetailAndListMatchHousekeepingRoomData()
    {
        object[] room = { 5, "Lobby", "Welcome", 2, "owner", 3, 25, true, false, true, 0 };
        Assert.Equal(new object[] { false }, Writes(new HousekeepingRoomDetailComposer(null)));
        Assert.Equal(new object[] { true }.Concat(room), Writes(new HousekeepingRoomDetailComposer(Lobby)));
        Assert.Equal(new object[] { 2 }.Concat(room).Concat(room), Writes(new HousekeepingRoomListComposer(new[] { Lobby, Lobby })));
    }

    [Fact]
    public void DashboardMatchesHousekeepingDashboardParser() =>
        Assert.Equal(new object[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, "Plus Emulator 3.4.3.0" },
            Writes(new HousekeepingDashboardComposer(new(1, 2, 3, 4, 5, 6, 7, 8, 9, "Plus Emulator 3.4.3.0"))));

    [Fact]
    public void ActionLogMatchesHousekeepingActionLogEntryData()
    {
        var entry = new HousekeepingAuditEntry { Id = 4, CreatedAt = DateTimeOffset.FromUnixTimeSeconds(100), ActorId = 1, ActorName = "staff", TargetType = "room", TargetId = 5, TargetLabel = "Lobby", Action = "room.close", Detail = "open=False", Success = true };
        Assert.Equal(new object[] { 1, 4, 100, 1, "staff", "room", 5, "Lobby", "room.close", "open=False", true },
            Writes(new HousekeepingActionLogComposer(new[] { entry })));
    }

    [Fact]
    public void ActionLogTimestampsUseTheLegacySecondsEdges()
    {
        var entries = new[]
        {
            Entry(1, null),
            Entry(2, new DateTimeOffset(1969, 12, 31, 23, 59, 59, TimeSpan.Zero)),
            Entry(3, DateTimeOffset.UnixEpoch),
            Entry(4, new DateTimeOffset(2038, 1, 19, 3, 14, 7, TimeSpan.Zero)),
            Entry(5, new DateTimeOffset(2038, 1, 19, 3, 14, 8, TimeSpan.Zero)),
            Entry(6, DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddTicks(9_999_999)),
        };

        Assert.Equal(new[] { 0, 0, 0, 2_147_483_647, int.MaxValue, 1_700_000_000 }, ActionLogTimestamps(Writes(new HousekeepingActionLogComposer(entries))));
    }

    [Fact]
    public void ActionLogFreezesItsEntriesAtConstruction()
    {
        var source = new List<HousekeepingAuditEntry> { Entry(1, DateTimeOffset.FromUnixTimeSeconds(100)) };
        var composer = new HousekeepingActionLogComposer(source);
        var first = Writes(composer);

        source.Clear();
        source.Add(Entry(9, DateTimeOffset.FromUnixTimeSeconds(999)));

        Assert.Equal(first, Writes(composer));
        Assert.Equal(first, Writes(composer));
        Assert.Equal(new[] { 100 }, ActionLogTimestamps(first));
    }

    private static HousekeepingAuditEntry Entry(int id, DateTimeOffset? createdAt) => new()
    {
        Id = id,
        CreatedAt = createdAt,
        ActorId = 1,
        ActorName = "staff",
        TargetType = "room",
        TargetId = 5,
        TargetLabel = "Lobby",
        Action = "room.close",
        Detail = "open=False",
        Success = true,
    };

    // Each action-log entry writes ten values, and the timestamp is the second value after the leading count.
    private static int[] ActionLogTimestamps(IReadOnlyList<object> writes) =>
        Enumerable.Range(0, (writes.Count - 1) / 10).Select(index => (int)writes[1 + index * 10 + 1]).ToArray();

    [Fact]
    public void AuditTargetTypesUseTheClientVocabulary()
    {
        Assert.Equal("user", HousekeepingAuditLog.TargetTypeName(HousekeepingTargetType.User));
        Assert.Equal("room", HousekeepingAuditLog.TargetTypeName(HousekeepingTargetType.Room));
        Assert.Equal("hotel", HousekeepingAuditLog.TargetTypeName(HousekeepingTargetType.Hotel));
    }

    // IDs from Octane-Renderer OutgoingHeader.ts / IncomingHeader.ts (HOUSEKEEPING_*).
    private static readonly Dictionary<string, uint> Incoming = new()
    {
        ["HousekeepingFindUserByNameEvent"] = 9100,
        ["HousekeepingFindUserByIdEvent"] = 9101,
        ["HousekeepingBanUserEvent"] = 9102,
        ["HousekeepingUnbanUserEvent"] = 9103,
        ["HousekeepingMuteUserEvent"] = 9104,
        ["HousekeepingKickUserEvent"] = 9105,
        ["HousekeepingForceDisconnectUserEvent"] = 9106,
        ["HousekeepingTradeLockUserEvent"] = 9108,
        ["HousekeepingResetUserPasswordEvent"] = 9109,
        ["HousekeepingFindRoomByIdEvent"] = 9110,
        ["HousekeepingSearchRoomsEvent"] = 9111,
        ["HousekeepingRoomStateEvent"] = 9112,
        ["HousekeepingMuteRoomEvent"] = 9113,
        ["HousekeepingKickAllFromRoomEvent"] = 9114,
        ["HousekeepingTransferRoomOwnershipEvent"] = 9115,
        ["HousekeepingDeleteRoomEvent"] = 9116,
        ["HousekeepingGiveCreditsEvent"] = 9117,
        ["HousekeepingGiveCurrencyEvent"] = 9118,
        ["HousekeepingGrantItemEvent"] = 9119,
        ["HousekeepingSetHcSubscriptionEvent"] = 9120,
        ["HousekeepingSendHotelAlertEvent"] = 9121,
        ["HousekeepingGetDashboardEvent"] = 9122,
        ["HousekeepingListActionLogEvent"] = 9123,
        ["HousekeepingGetRolesEvent"] = 9130,
        ["HousekeepingGetRoleMembersEvent"] = 9131,
        ["HousekeepingGetUserOverridesEvent"] = 9132,
        ["HousekeepingGetRolesAuditEvent"] = 9133,
        ["HousekeepingSaveRoleEvent"] = 9134,
        ["HousekeepingDeleteRoleEvent"] = 9135,
        ["HousekeepingSetRolePermissionEvent"] = 9136,
        ["HousekeepingSetRoleLimitEvent"] = 9137,
        ["HousekeepingAssignRoleEvent"] = 9138,
        ["HousekeepingRevokeRoleEvent"] = 9139,
        ["HousekeepingSetUserOverrideEvent"] = 9140,
        ["HousekeepingRemoveUserOverrideEvent"] = 9141
    };

    private static readonly Dictionary<string, uint> Outgoing = new()
    {
        ["HousekeepingUserDetailComposer"] = 9200,
        ["HousekeepingActionResultComposer"] = 9201,
        ["HousekeepingRoomDetailComposer"] = 9202,
        ["HousekeepingRoomListComposer"] = 9203,
        ["HousekeepingDashboardComposer"] = 9204,
        ["HousekeepingActionLogComposer"] = 9205,
        ["HousekeepingRolesComposer"] = 9210,
        ["HousekeepingRoleMembersComposer"] = 9211,
        ["HousekeepingUserOverridesComposer"] = 9212,
        ["HousekeepingRolesAuditComposer"] = 9213
    };

    [Fact]
    public void EveryHandlerAndItsDependenciesAreRegisteredByProgram()
    {
        // Mirrors Program.Main: [Singleton] interfaces plus every class with a matching I{Name} interface.
        var services = new ServiceCollection();
        services.AddAssignableTo(typeof(Program).Assembly, typeof(IPacketEvent));
        services.Scan(scan => scan.FromAssemblies(typeof(Program).Assembly)
            .AddClasses(classes => classes.Where(c => c.GetInterface($"I{c.Name}") != null))
            .UsingRegistrationStrategy(Scrutor.RegistrationStrategy.Skip).AsSelfWithInterfaces().WithSingletonLifetime());
        services.AddAssignableTo(typeof(Program).Assembly, typeof(HabboHotel.Users.Authentication.IPasswordHasher));

        foreach (var name in Incoming.Keys) {
            var handler = typeof(HousekeepingBanUserEvent).Assembly.GetType($"{typeof(HousekeepingBanUserEvent).Namespace}.{name}")!;
            // AsSelfWithInterfaces registers the handler itself and forwards IPacketEvent to that registration.
            Assert.Contains(services, service => service.ServiceType == handler && service.ImplementationType == handler);

            foreach (var parameter in handler.GetConstructors().Single().GetParameters()) {
                Assert.True(services.Any(service => service.ServiceType == parameter.ParameterType), $"{name} needs {parameter.ParameterType.Name}");
            }
        }
    }

    [Fact]
    public void EveryHandlerIsRegisteredUnderTheRendererHeaderInTheOctaneRevision()
    {
        var handlers = typeof(HousekeepingBanUserEvent).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(HousekeepingBanUserEvent).Namespace && typeof(IPacketEvent).IsAssignableFrom(type))
            .Select(type => type.Name).ToHashSet();
        Assert.Equal(Incoming.Keys.ToHashSet(), handlers);
        using var revision = JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Resources/Revisions/example.json")));

        foreach (var (name, id) in Incoming) {
            Assert.Equal(id, (uint)typeof(ClientPacketHeader).GetField(name)!.GetRawConstantValue()!);
            Assert.Equal(id, revision.RootElement.GetProperty("IncomingHeaders").GetProperty(name).GetUInt32());
        }

        foreach (var (name, id) in Outgoing) {
            Assert.Equal(id, (uint)typeof(ServerPacketHeader).GetField(name)!.GetRawConstantValue()!);
            Assert.Equal(id, revision.RootElement.GetProperty("OutgoingHeaders").GetProperty(name).GetUInt32());
        }
    }
}

public class ClientPermissionWireTests
{
    [Fact]
    public void ResolvedMetadataAndConcreteGrantsFollowTheRendererBlock()
    {
        var access = UserAccess.Create([new(new AccessRole(9, "developer", "Developer", 90, 7, "DEV", true,
            [PermissionKeys.Ambassador, PermissionKeys.CameraUse, PermissionKeys.HousekeepingAccess], new Dictionary<string, int>()))]);
        var packet = new RecordingPacket();
        new UserRightsComposer(UserRightsSnapshot.Capture(access)).Compose(packet);
        Assert.Equal(new object[] { 0, 7, true, 9, "Developer", "DEV", 3, "ambassador", 1, "camera.use", 1, "housekeeping.access", 1 }, packet.Writes);
    }
}
