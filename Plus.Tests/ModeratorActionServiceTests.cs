using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ModeratorActionServiceTests
{
    [Fact]
    public void MutePublishesOnlyAfterPersistenceAndDoesNotOverflowMinuteConversion()
    {
        var (actor, _) = HabbiconTestSupport.Client(new Habbo { Id = 7, Access = EditorTestSupport.Access([], 90) });
        var target = new Habbo { Id = 8, TimeMuted = 12, Access = EditorTestSupport.Access([], 50) };
        var (client, sent) = HabbiconTestSupport.Client(target); target.Client = client;
        var store = new RecordingStore { BeforeWrite = () => { Assert.Equal(12, target.TimeMuted); Assert.Empty(sent); } };
        var service = Service(target, store);
        service.Mute(actor, target.Id, int.MaxValue);
        var seconds = (long)int.MaxValue * 60;
        Assert.Equal((target.Id, seconds), Assert.Single(store.Mutes));
        Assert.Equal((double)seconds, target.TimeMuted);
        Assert.Single(sent);
    }

    [Fact]
    public void MutePersistenceFailureCannotPublishDurationOrNotification()
    {
        var (actor, _) = HabbiconTestSupport.Client(new Habbo { Id = 7, Access = EditorTestSupport.Access([], 90) });
        var target = new Habbo { Id = 8, TimeMuted = 12, Access = EditorTestSupport.Access([], 50) };
        var (client, sent) = HabbiconTestSupport.Client(target); target.Client = client;
        Assert.Throws<InvalidOperationException>(() => Service(target, new() { Fail = true }).Mute(actor, target.Id, 10));
        Assert.Equal(12, target.TimeMuted);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(90, 1)]
    [InlineData(100, 1)]
    [InlineData(50, -1)]
    public void EqualHigherOrInvalidMuteRequestsDoNotWrite(int targetWeight, int minutes)
    {
        var (actor, _) = HabbiconTestSupport.Client(new Habbo { Id = 7, Access = EditorTestSupport.Access([], 90) });
        var target = new Habbo { Id = 8, TimeMuted = 12, Access = EditorTestSupport.Access([], targetWeight) };
        var store = new RecordingStore();
        Service(target, store).Mute(actor, target.Id, minutes);
        Assert.Empty(store.Mutes);
        Assert.Equal(12, target.TimeMuted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoomModerationCommitsBeforePublishingSettings(bool fail)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42; room.OwnerId = 7; room.Name = "Original"; room.Description = "Description"; room.Tags.Add("bad");
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        var (actor, _) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var store = new RecordingStore { Fail = fail, BeforeWrite = () => { Assert.Equal("Original", room.Name); Assert.Equal(RoomAccess.Open, room.Access); Assert.Single(room.Tags); } };
        var rooms = Proxy<IRoomManager>((method, args) => { Assert.Equal("TryGetRoom", method); args[1] = room; return true; });
        var service = new ModeratorActionService(null!, null!, rooms, null!, null!, store);
        if (fail)
        {
            Assert.Throws<InvalidOperationException>(() => service.ModerateRoom(actor, new(42, true, true, false)));
            Assert.Equal("Original", room.Name); Assert.Equal(RoomAccess.Open, room.Access); Assert.Single(room.Tags);
        }
        else
        {
            service.ModerateRoom(actor, new(42, true, true, false));
            Assert.Equal(ModeratorActionService.InappropriateRoomText, room.Name);
            Assert.Equal(ModeratorActionService.InappropriateRoomText, room.Description);
            Assert.Equal(RoomAccess.Doorbell, room.Access); Assert.Empty(room.Tags);
        }
    }

    [Fact]
    public async Task IncomingHandlersDecodeValuesAndDiscardJunkBeforeDelegation()
    {
        var service = new RecordingService();
        var (actor, _) = HabbiconTestSupport.Client(new Habbo());
        await new ModerationCautionEvent(service).Parse(actor, HabbiconTestSupport.Incoming(8, "warning"));
        await new ModerationMuteEvent(service).Parse(actor, HabbiconTestSupport.Incoming(8, "discard", 10, "junk1", "junk2"));
        await new ModerationKickEvent(service).Parse(actor, HabbiconTestSupport.Incoming(8, "discard"));
        await new ModerateRoomEvent(service).Parse(actor, HabbiconTestSupport.Incoming(42, 1, 0, 1));
        Assert.Equal((8, "warning"), service.CautionRequest);
        Assert.Equal((8, 10), service.MuteRequest);
        Assert.Equal(8, service.KickId);
        Assert.Equal(new ModerateRoomRequest(42, true, false, true), service.RoomRequest);
    }

    private static ModeratorActionService Service(Habbo target, RecordingStore store) =>
        new(null!, Proxy<IModeratorUserLookup>((method, _) => method == "GetById" ? target : throw new NotSupportedException(method)), null!, null!, null!, store);
    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)proxy).Call = call; return proxy;
    }
    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }
    private sealed class RecordingStore : IModeratorActionStore
    {
        public bool Fail { get; init; }
        public Action? BeforeWrite { get; init; }
        public List<(int Id, long Seconds)> Mutes { get; } = [];
        private void Persist() { BeforeWrite?.Invoke(); if (Fail) throw new InvalidOperationException("forced failure"); }
        public void AddCaution(int userId) => Persist();
        public void SetMute(int userId, long seconds) { Persist(); Mutes.Add((userId, seconds)); }
        public void ModerateRoom(uint roomId, bool rename, bool locked, bool endPromotion) => Persist();
    }
    private sealed class RecordingService : IModeratorActionService
    {
        public (int, string)? CautionRequest; public (int, int)? MuteRequest; public int? KickId; public ModerateRoomRequest? RoomRequest;
        public void Caution(GameClient actor, int targetId, string message) => CautionRequest = (targetId, message);
        public void Mute(GameClient actor, int targetId, int minutes) => MuteRequest = (targetId, minutes);
        public void Kick(GameClient actor, int targetId) => KickId = targetId;
        public void ModerateRoom(GameClient actor, ModerateRoomRequest request) => RoomRequest = request;
    }
}
