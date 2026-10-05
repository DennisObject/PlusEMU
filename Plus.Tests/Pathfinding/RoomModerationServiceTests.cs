using System.Collections.Concurrent;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Action;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task RoomModerationHandlersDecodeFullyAndOnlyDelegate()
    {
        var moderation = new RecordingRoomModeration();
        await new KickUserEvent(moderation).Parse(null!, ClientPacket(8));
        await new BanUserEvent(moderation).Parse(null!, ClientPacket(8, 999, "hour"));
        await new LetUserInEvent(moderation).Parse(null!, null!, ClientPacket("guest", true));
        Assert.Equal(new[] { "kick 8", "ban 8 999 hour", "door guest True" }, moderation.Calls);
        moderation.Calls.Clear();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new BanUserEvent(moderation).Parse(null!, ClientPacket(8, 999)));
        Assert.Empty(moderation.Calls);
    }

    [Fact]
    public void RoomKickPublishesRemovalBeforeAchievementAndPreservesRightsAndOwnerProtection()
    {
        var target = ModerationTarget();
        var achievements = new List<string>();
        var service = ModerationService(target, name =>
        {
            Assert.Null(target.GetHabbo().CurrentRoom);
            Assert.Null(_room.GetRoomUserManager().GetRoomUserByHabbo(8));
            achievements.Add(name);
        });
        _room.OwnerName = "somebody else";
        service.Kick(_client, 8);
        Assert.Same(_room, target.GetHabbo().CurrentRoom);
        Assert.Empty(target.Sent);
        _room.OwnerName = "owner";
        service.Kick(_client, 7);
        service.Kick(_client, 404);
        Assert.Empty(achievements);
        service.Kick(_client, 8);
        Assert.Equal(new[] { "ACH_SelfModKickSeen" }, achievements);
        Assert.Equal(new[] { ServerPacketHeader.GenericErrorComposer, ServerPacketHeader.CloseConnectionComposer }, target.Sent.Take(2));
    }

    [Theory]
    [InlineData("Hour", 3600)]
    [InlineData("day", 86400)]
    [InlineData("perm", 78892200)]
    [InlineData("unknown", 0)]
    public void RoomBanCommitsBeforeRemovalAndAchievementUsingTheLegacyDurationPolicy(string value, int seconds)
    {
        var target = ModerationTarget();
        var now = DateTimeOffset.Parse("2040-01-02T03:04:05.123456Z");
        var clock = new ModerationClock(now);
        var store = new RecordingModerationBans
        {
            BeforeSave = () =>
            {
                Assert.Same(_room, target.GetHabbo().CurrentRoom);
                Assert.NotNull(_room.GetRoomUserManager().GetRoomUserByHabbo(8));
                Assert.Empty(target.Sent);
            }
        };
        _room.SetBans(new BansComponent(_room, store, clock, []));
        var achievements = new List<string>();
        var service = ModerationService(target, name =>
        {
            Assert.Null(target.GetHabbo().CurrentRoom);
            Assert.Equal(1, _room.GetBans().Count);
            achievements.Add(name);
        });
        service.Ban(_client, new(8, 999, value));
        Assert.Equal((RoomId, 8, now.AddSeconds(seconds)), Assert.Single(store.Saves));
        Assert.Equal(1, clock.Reads);
        Assert.Equal(new[] { "ACH_SelfModBanSeen" }, achievements);
    }

    [Fact]
    public void RoomBanStoreFailureLeavesActorBanCacheAndPacketsUnchanged()
    {
        var target = ModerationTarget();
        var store = new RecordingModerationBans { Fail = true };
        _room.SetBans(new BansComponent(_room, store, new ModerationClock(DateTimeOffset.Parse("2040-01-01T00:00:00Z")), []));
        var achievements = new List<string>();
        var service = ModerationService(target, achievements.Add);
        Assert.Throws<InvalidOperationException>(() => service.Ban(_client, new(8, 42, "hour")));
        Assert.Same(_room, target.GetHabbo().CurrentRoom);
        Assert.NotNull(_room.GetRoomUserManager().GetRoomUserByHabbo(8));
        Assert.Equal(0, _room.GetBans().Count);
        Assert.Empty(target.Sent);
        Assert.Empty(_client.Sent);
        Assert.Empty(achievements);
    }

    [Fact]
    public void RoomSanctionsRejectHigherRankAndDepartedOrDisconnectedTargets()
    {
        var target = ModerationTarget();
        target.GetHabbo().Access = EditorTestSupport.Access([], 10);
        var calls = 0;
        var service = ModerationService(target, _ => calls++);
        service.Kick(_client, 8);
        service.Ban(_client, new(8, 42, "day"));
        target.GetHabbo().Access = UserAccess.Empty;
        target.GetHabbo().CurrentRoom = null;
        service.Kick(_client, 8);
        service.Ban(_client, new(8, 42, "day"));
        target.GetHabbo().CurrentRoom = _room;
        var disconnected = new RoomModerationService(Proxy<IAchievementManager>((_, _) => throw new InvalidOperationException()),
            Proxy<IGameClientManager>((method, _) => method == "GetClientByUserId" ? null : throw new InvalidOperationException()));
        disconnected.Kick(_client, 8);
        disconnected.Ban(_client, new(8, 42, "day"));
        Assert.Empty(target.Sent);
        Assert.Empty(_client.Sent);
        Assert.Equal(0, calls);
        Assert.NotNull(_room.GetRoomUserManager().GetRoomUserByHabbo(8));
    }

    [Fact]
    public void ReplacementSessionCannotBeSanctionedThroughThePreviousSessionsRoomActor()
    {
        var previous = ModerationTarget();
        var replacement = new TestClient();
        replacement.SetHabbo(new Habbo { Id = 8, Username = "target", CurrentRoom = _room, Access = UserAccess.Empty });
        var service = ModerationService(replacement, _ => throw new InvalidOperationException("unexpected achievement"));
        service.Kick(_client, 8);
        service.Ban(_client, new(8, 42, "hour"));
        Assert.Same(_room, previous.GetHabbo().CurrentRoom);
        Assert.Same(_room, replacement.GetHabbo().CurrentRoom);
        Assert.NotNull(_room.GetRoomUserManager().GetRoomUserByHabbo(8));
        Assert.Empty(previous.Sent);
        Assert.Empty(replacement.Sent);
        Assert.Empty(_client.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoomDoorAnswersSendToTheTargetBeforeTheRightsBroadcast(bool accepted)
    {
        var target = ModerationTarget();
        var service = ModerationService(target, _ => throw new InvalidOperationException());
        var header = accepted ? ServerPacketHeader.FlatAccessibleComposer : ServerPacketHeader.FlatAccessDeniedComposer;
        _client.BeforeCapture = id =>
        {
            Assert.Equal(header, id);
            Assert.Equal(header, Assert.Single(target.Sent));
        };
        service.AnswerDoor(_room, _client, "target", accepted);
        Assert.Equal(accepted, target.GetHabbo().RoomAuthOk);
        Assert.Equal(header, Assert.Single(_client.Sent));
        var personal = new FlashIncomingPacket { Buffer = target.Packets.Single().Body };
        Assert.Equal("", personal.ReadString());
        Assert.False(personal.HasDataRemaining());
        var broadcast = new FlashIncomingPacket { Buffer = _client.Packets.Single().Body };
        Assert.Equal("target", broadcast.ReadString());
        Assert.False(broadcast.HasDataRemaining());
    }

    [Fact]
    public void RoomDoorDenialsNeverLookupTargetsOrPublish()
    {
        var service = new RoomModerationService(null!, Proxy<IGameClientManager>((_, _) => throw new InvalidOperationException("unexpected lookup")));
        _room.OwnerName = "other";
        _room.UsersWithRights = [];
        service.AnswerDoor(_room, _client, "target", true);
        _room.OwnerName = "owner";
        _client.GetHabbo().CurrentRoom = null;
        service.AnswerDoor(_room, _client, "target", true);
        Assert.Empty(_client.Sent);
    }

    private TestClient ModerationTarget()
    {
        var owner = Viewer();
        owner.UserId = 7;
        owner.InternalRoomId = owner.VirtualId;
        _room.UsersWithRights = [];
        var client = new TestClient();
        client.SetHabbo(new Habbo { Id = 8, Username = "target", CurrentRoom = _room, Access = UserAccess.Empty });
        var user = new RoomUser(8, RoomId, 2, _room) { UserId = 8, InternalRoomId = 2, X = 1, Y = 1 };
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(user, client);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        users[2] = user;
        return client;
    }

    private RoomModerationService ModerationService(GameClient target, Action<string> progress) => new(
        Proxy<IAchievementManager>((method, args) =>
        {
            Assert.Equal(nameof(IAchievementManager.ProgressAchievement), method);
            progress((string)args[1]!);
            return null;
        }),
        Proxy<IGameClientManager>((method, args) => method switch
        {
            "GetClientByUserId" => (int)args[0]! == 8 ? target : null,
            "GetClientByUsername" => (string)args[0]! == "target" ? target : null,
            _ => throw new InvalidOperationException(method)
        }));

    private sealed class RecordingRoomModeration : IRoomModerationService
    {
        public List<string> Calls = [];
        public void Kick(GameClient session, int userId) => Calls.Add($"kick {userId}");
        public void Ban(GameClient session, RoomBanRequest request) => Calls.Add($"ban {request.UserId} {request.RoomId} {request.Duration}");
        public void AnswerDoor(Room room, GameClient session, string username, bool accepted) => Calls.Add($"door {username} {accepted}");
    }

    private sealed class ModerationClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads;
        public override DateTimeOffset GetUtcNow() { Reads++; return now; }
    }

    private sealed class RecordingModerationBans : IRoomBanStore
    {
        public Action? BeforeSave;
        public bool Fail;
        public List<(uint RoomId, int UserId, DateTimeOffset ExpiresAt)> Saves = [];
        public IEnumerable<RoomBan> Load(uint roomId) => throw new NotSupportedException();
        public void Save(uint roomId, int userId, DateTimeOffset expiresAt)
        {
            BeforeSave?.Invoke();
            Saves.Add((roomId, userId, expiresAt));
            if (Fail) throw new InvalidOperationException("forced ban failure");
        }
        public void Delete(uint roomId, int userId) => throw new NotSupportedException();
        public IEnumerable<int> ActiveUserIds(uint roomId) => throw new NotSupportedException();
    }
}
