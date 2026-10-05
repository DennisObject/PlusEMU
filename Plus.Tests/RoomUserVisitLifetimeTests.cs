using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void AdmissionCapturesTheExactSessionInsteadOfTheGlobalSameAccountClient()
    {
        var globalReplacement = new TestClient();
        globalReplacement.SetHabbo(new Habbo { Id = 7, Username = "replacement", CurrentRoom = _room });
        var previous = (IGame)_gameField.GetValue(null)!;
        var clients = Proxy<IGameClientManager>((method, args) => method == "GetClientByUserId" && (int)args[0]! == 7
            ? globalReplacement
            : throw new InvalidOperationException(method));
        _gameField.SetValue(null, Proxy<IGame>((method, args) => method == "get_ClientManager"
            ? clients
            : typeof(IGame).GetMethod(method)!.Invoke(previous, args)));

        Assert.True(_room.GetRoomUserManager().AddAvatarToRoom(_client));

        var admitted = _room.GetRoomUserManager().GetRoomUserByHabbo(7);
        Assert.NotNull(admitted);
        admitted = admitted!;
        Assert.Same(_client, admitted.GetClient());
        Assert.NotSame(globalReplacement, admitted.GetClient());
    }

    [Fact]
    public void CapturedVisitFollowsLiveNameAndNeverRebindsAfterDetach()
    {
        _gameField.SetValue(null, Proxy<IGame>((method, _) => throw new InvalidOperationException(method)));
        var user = new RoomUser(7, RoomId, 17, _room, _client);

        Assert.Same(_client, user.GetClient());
        _client.GetHabbo().Username = "renamed";
        Assert.Equal("renamed", user.GetUsername());

        user.Dispose();
        user.Dispose();
        _client.Sent.Clear();
        user.OnChat(1, "stale", false);
        user.CarryItem(4);

        Assert.Null(user.GetClient());
        Assert.Equal("Unknown User", user.GetUsername());
        Assert.Empty(_client.Sent);
        Assert.Null(RoomOf(user));
    }

    [Fact]
    public void SameAccountReplacementCannotBeRemovedByTheOldSession()
    {
        var manager = _room.GetRoomUserManager();
        var oldClient = new TestClient();
        oldClient.SetHabbo(new Habbo { Id = 7, Username = "old", CurrentRoom = _room });
        var replacementClient = new TestClient();
        replacementClient.SetHabbo(new Habbo { Id = 7, Username = "replacement", CurrentRoom = _room });
        var replacement = new RoomUser(7, RoomId, 19, _room, replacementClient) { InternalRoomId = 19 };
        Users(manager)[19] = replacement;

        manager.RemoveUserFromRoom(oldClient, true, true);

        Assert.Same(replacement, manager.GetRoomUserByVirtualId(19));
        Assert.Same(_room, oldClient.GetHabbo().CurrentRoom);
        Assert.Same(_room, replacementClient.GetHabbo().CurrentRoom);
        Assert.Empty(oldClient.Sent);
        Assert.Empty(replacementClient.Sent);
        Assert.Same(replacementClient, replacement.GetClient());
    }

    [Fact]
    public void ExitFailureDetachesOnlyAfterRemovedVisitCallbacks()
    {
        var store = new ThrowingExitStore();
        var manager = new RoomUserManager(_room, store, TimeProvider.System);
        Set("_roomUserManager", manager);
        var user = new RoomUser(7, RoomId, 23, _room, _client) { InternalRoomId = 23, UserId = 7 };
        store.User = user;
        store.Room = _room;
        store.Client = _client;
        Users(manager)[23] = user;

        manager.RemoveUserFromRoom(_client, false);

        Assert.True(store.RecordExitCalled);
        Assert.True(store.SawCapturedVisit);
        Assert.Null(manager.GetRoomUserByVirtualId(23));
        Assert.Null(user.GetClient());
        Assert.Null(RoomOf(user));
    }

    [Fact]
    public void ManagerDisposalDetachesHumanBotAndPetAndIsRepeatable()
    {
        var manager = _room.GetRoomUserManager();
        var human = new RoomUser(7, RoomId, 1, _room, _client) { InternalRoomId = 1 };
        var bot = new RoomUser(0, RoomId, 2, _room, null)
        {
            InternalRoomId = 2,
            BotData = Bot(BotAiType.Generic, 2)
        };
        var pet = new RoomUser(0, RoomId, 3, _room, null)
        {
            InternalRoomId = 3,
            BotData = Bot(BotAiType.Pet, 3),
            PetData = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet))
        };
        Users(manager)[1] = human;
        Users(manager)[2] = bot;
        Users(manager)[3] = pet;

        manager.Dispose();
        manager.Dispose();

        Assert.Null(human.GetClient());
        Assert.Null(RoomOf(human));
        Assert.Null(RoomOf(bot));
        Assert.Null(RoomOf(pet));
    }

    [Fact]
    public void RemovingAnAbandonedVisitCannotEraseItsReplacementEntry()
    {
        var manager = _room.GetRoomUserManager();
        var abandonedClient = new TestClient();
        abandonedClient.SetHabbo(new Habbo { Id = 7, Username = "abandoned", CurrentRoom = null });
        var abandoned = new RoomUser(7, RoomId, 31, _room, abandonedClient) { InternalRoomId = 31 };
        var replacement = new RoomUser(8, RoomId, 31, _room, _client) { InternalRoomId = 31 };
        Users(manager)[31] = replacement;
        _client.Sent.Clear();

        Assert.False(manager.ValidateMovementActor(abandoned));

        Assert.Same(replacement, manager.GetRoomUserByVirtualId(31));
        Assert.DoesNotContain(ServerPacketHeader.UserRemoveComposer, _client.Sent);
        Assert.Null(abandoned.GetClient());
        Assert.Null(RoomOf(abandoned));
        Assert.Same(_client, replacement.GetClient());
    }

    [Fact]
    public void HumanChatRejectsACapturedClientAfterItLeavesTheCanonicalRoom()
    {
        var user = new RoomUser(7, RoomId, 41, _room, _client);
        _client.GetHabbo().CurrentRoom = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        _client.GetHabbo().HasSpoken = false;
        _client.Sent.Clear();

        user.OnChat(1, "stale", false);

        Assert.False(_client.GetHabbo().HasSpoken);
        Assert.Empty(_client.Sent);
        Assert.Same(_client, user.GetClient());
    }

    [Fact]
    public void DisposalStoreFailureStillDetachesTheOwnedVisits()
    {
        var store = new ThrowingExitStore { ThrowOnUserCount = true };
        var manager = new RoomUserManager(_room, store, TimeProvider.System);
        Set("_roomUserManager", manager);
        var user = new RoomUser(7, RoomId, 51, _room, _client) { InternalRoomId = 51 };
        store.User = user;
        store.Room = _room;
        store.Client = _client;
        Users(manager)[51] = user;

        Assert.Throws<InvalidOperationException>(manager.Dispose);
        manager.Dispose();

        Assert.True(store.SawCapturedVisit);
        Assert.Null(user.GetClient());
        Assert.Null(RoomOf(user));
    }

    [Fact]
    public void ThrowingBotLeaveCallbackSeesTheVisitBeforeTerminalDetach()
    {
        var manager = _room.GetRoomUserManager();
        var bot = new RoomUser(0, RoomId, 61, _room, null)
        {
            InternalRoomId = 61,
            BotData = Bot(BotAiType.Generic, 61)
        };
        var sawCapturedRoom = false;
        bot.BotAi = new ThrowingLeaveBotAi(() => sawCapturedRoom = ReferenceEquals(_room, RoomOf(bot)));
        Users(manager)[61] = bot;

        Assert.Throws<InvalidOperationException>(() => manager.RemoveBot(61, false));

        Assert.True(sawCapturedRoom);
        Assert.Null(manager.GetRoomUserByVirtualId(61));
        Assert.Null(RoomOf(bot));
    }

    [Fact]
    public void DetachedAndDisposedManagerBotsAreNeverAdmittedForMovement()
    {
        var manager = _room.GetRoomUserManager();
        var detached = new RoomUser(0, RoomId, 71, _room, null)
        {
            InternalRoomId = 71,
            BotData = Bot(BotAiType.Generic, 71)
        };
        detached.Dispose();
        _client.Sent.Clear();

        Assert.False(manager.ValidateMovementActor(detached));
        Assert.Empty(_client.Sent);

        var oldBot = new RoomUser(0, RoomId, 72, _room, null)
        {
            InternalRoomId = 72,
            BotData = Bot(BotAiType.Generic, 72)
        };
        Users(manager)[72] = oldBot;
        manager.Dispose();
        _client.Sent.Clear();

        Assert.False(manager.ValidateMovementActor(oldBot));
        Assert.Empty(_client.Sent);
        Assert.Null(RoomOf(oldBot));
    }

    private static ConcurrentDictionary<int, RoomUser> Users(RoomUserManager manager) =>
        (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;

    private static Room? RoomOf(RoomUser user) =>
        (Room?)typeof(RoomUser).GetField("_mRoom", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(user);

    private static RoomBot Bot(BotAiType type, int id)
    {
        var bot = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        bot.AiType = type;
        bot.Id = id;
        bot.BotId = id;
        return bot;
    }

    private sealed class ThrowingExitStore : IRoomUserStore
    {
        public RoomUser User { get; set; } = null!;
        public Room Room { get; set; } = null!;
        public GameClient Client { get; set; } = null!;
        public bool RecordExitCalled { get; private set; }
        public bool SawCapturedVisit { get; private set; }
        public bool ThrowOnUserCount { get; init; }

        public void UpdateUserCount(uint roomId, int count)
        {
            if (!ThrowOnUserCount)
                return;
            SawCapturedVisit = ReferenceEquals(Client, User.GetClient()) && ReferenceEquals(Room, RoomOf(User));
            throw new InvalidOperationException("forced user-count failure");
        }
        public void SavePet(RoomPetSave pet) { }
        public void SaveBot(RoomBotSave bot) { }
        public void RecordExit(uint roomId, int userId, DateTimeOffset exitedAt, int usersNow)
        {
            RecordExitCalled = true;
            SawCapturedVisit = ReferenceEquals(Client, User.GetClient()) && ReferenceEquals(Room, RoomOf(User));
            throw new InvalidOperationException("forced exit failure");
        }
    }

    private sealed class ThrowingLeaveBotAi(Action beforeThrow) : BotAi
    {
        public override void OnSelfEnterRoom() { }
        public override void OnSelfLeaveRoom(bool kicked)
        {
            beforeThrow();
            throw new InvalidOperationException("forced bot leave failure");
        }
        public override void OnUserEnterRoom(RoomUser user) { }
        public override void OnUserLeaveRoom(GameClient client) { }
        public override void OnUserSay(RoomUser user, string message) { }
        public override void OnUserShout(RoomUser user, string message) { }
        public override void OnTimerTick() { }
    }
}
