using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Incoming.Rooms.Settings;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RoomSettingsServiceTests
{
    [Fact]
    public void NonOwnerCannotChangeSettingsEvenWithMatchingUsername()
    {
        var room = Room();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 8, Username = "owner", Access = EditorTestSupport.Access([]) });
        var store = new Store();
        new RoomSettingsService(Rooms(room), null!, null!, null!, store, null!).Save(client, Request());
        Assert.Null(store.Saved);
        Assert.Equal("Original", room.Name);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerSettingsPersistBeforePublicationAndAllowEveryoneToKick(bool fail)
    {
        var room = Room();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "owner", Access = EditorTestSupport.Access([]) });
        var store = new Store { Fail = fail, Before = () => { Assert.Equal("Original", room.Name); Assert.Equal(0, room.WhoCanKick); Assert.Empty(sent); } };
        var service = new RoomSettingsService(Rooms(room), Proxy<IWordFilterManager>((_, a) => a[0]),
            Proxy<INavigatorManager>((method, a) => { Assert.Equal("TryGetSearchResultList", method); a[1] = null; return false; }),
            Proxy<IAchievementManager>((method, _) => method == "ProgressAchievement" ? true : throw new NotSupportedException(method)),
            store, Proxy<ISettingsManager>((_, _) => null));
        if (fail)
        {
            Assert.Throws<InvalidOperationException>(() => service.Save(client, Request()));
            Assert.Equal("Original", room.Name); Assert.Equal(0, room.WhoCanKick); Assert.Empty(sent);
        }
        else
        {
            service.Save(client, Request());
            Assert.Equal("Updated", room.Name); Assert.Equal(2, room.WhoCanKick);
            Assert.Equal(RoomAccess.Open, room.Access); // Empty password opens password mode.
            Assert.Equal(new[] { "one" }, room.Tags);
            Assert.Equal(3, sent.Count);
            Assert.Equal(2, store.Saved!.WhoKick);
            Assert.False(store.Saved.Hidewall); // Club-only geometry stays unavailable.
        }
    }

    [Fact]
    public async Task HandlerDecodesTheEntireTypedRequest()
    {
        var target = new RecordingService();
        var (client, _) = HabbiconTestSupport.Client(new Habbo());
        await new SaveRoomSettingsEvent(target).Parse(client, HabbiconTestSupport.Incoming(42, "Name", "Desc", 2, "secret", 25, 36,
            2, "ONE", "two", 1, true, false, true, false, -1, 1, 1, 2, 0, 1, 2, 0, 50, 2));
        var request = Assert.IsType<RoomSettingsRequest>(target.Request);
        Assert.Equal((uint)42, request.RoomId); Assert.Equal("secret", request.Password);
        Assert.Equal(new[] { "ONE", "two" }, request.Tags.ToArray());
        Assert.Equal(2, request.WhoKick); Assert.Equal(50, request.ChatDistance); Assert.Equal(2, request.ExtraFlood);
    }

    private static RoomSettingsRequest Request() => new(42, "Updated", "Description", 2, "", 25, 36, ["ONE"],
        1, true, false, true, true, 1, 1, 0, 2, 0, 0, 1, 2, 50, 1);
    private static Room Room()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42; room.OwnerId = 7; room.OwnerName = "owner"; room.Type = "private"; room.Name = "Original";
        Set("_roomItemHandling", new RoomItemHandling(room, TestRoomItemStore.Instance));
        Set("_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        Set("_gamemap", new Gamemap(room, new RoomModel("test", 0, 0, 0, 0, "00\r00", 0, 0, false), TestLogging.Navigation));
        return room;
        void Set(string field, object value) => typeof(Room).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, value);
    }
    private static IRoomManager Rooms(Room room) => Proxy<IRoomManager>((method, args) => { Assert.Equal("TryLoadRoom", method); args[1] = room; return true; });
    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)proxy).Call = call; return proxy;
    }
    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }
    private sealed class Store : IRoomSettingsStore
    {
        public bool Fail { get; init; } public Action? Before { get; init; } public RoomSettingsRequest? Saved;
        public void Save(RoomSettingsRequest values, int ownerId, RoomAccess access)
        { Before?.Invoke(); if (Fail) throw new InvalidOperationException("forced failure"); Saved = values; }
    }
    private sealed class RecordingService : IRoomSettingsService
    {
        public RoomSettingsRequest? Request;
        public void Save(GameClient session, RoomSettingsRequest request) => Request = request;
    }
}
