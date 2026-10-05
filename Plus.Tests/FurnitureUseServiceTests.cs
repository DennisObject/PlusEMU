using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Data.Toner;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class FurnitureUseServiceTests
{
    [Fact]
    public void TonerTogglePersistsBeforePublishingAndCanToggleBack()
    {
        var (room, client, item) = Context();
        var store = new Store(() => Assert.Equal(0, room.TonerData.Enabled));
        var service = new FurnitureUseService(store, null!);
        service.Use(room, client, new(item.Id, 0));
        Assert.Equal(1, room.TonerData.Enabled);
        Assert.Equal((item.Id, room.Id, true), Assert.Single(store.Values));
        new FurnitureUseService(new Store(), null!).Use(room, client, new(item.Id, 0));
        Assert.Equal(0, room.TonerData.Enabled);
    }

    [Fact]
    public void FailedPersistenceLeavesTonerStateUntouched()
    {
        var (room, client, item) = Context();
        var store = new Store { Fail = true };
        Assert.Throws<InvalidOperationException>(() => new FurnitureUseService(store, null!).Use(room, client, new(item.Id, 0)));
        Assert.Equal(0, room.TonerData.Enabled);
        Assert.Equal(1u, Assert.Single(store.Values).Item1);
    }

    [Theory]
    [InlineData("rights")]
    [InlineData("room")]
    [InlineData("toner")]
    [InlineData("item")]
    public void InvalidContextDoesNotPersist(string invalid)
    {
        var (room, client, item) = Context();
        if (invalid == "rights") room.OwnerName = "another-owner";
        if (invalid == "room") client.GetHabbo().CurrentRoom = null;
        if (invalid == "toner") room.TonerData.ItemId++;
        if (invalid == "item") item.RoomId++;
        var store = new Store();
        new FurnitureUseService(store, null!).Use(room, client, new(item.Id, 0));
        Assert.Empty(store.Values);
        Assert.Equal(0, room.TonerData.Enabled);
    }

    private static (Room Room, GameClient Client, Item Item) Context()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9; room.OwnerName = "owner"; room.Type = "private"; room.UsersWithRights = [];
        Set(room, "_roomItemHandling", new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems));
        Set(room, "_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused));
        var item = new Item { Id = 1, RoomId = room.Id, OwnerId = 7, Definition = new() { InteractionType = InteractionType.Toner }, ExtraData = new LegacyDataFormat() };
        Set(item, "_room", room);
        var items = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        items[item.Id] = item;
        room.TonerData = (TonerData)RuntimeHelpers.GetUninitializedObject(typeof(TonerData));
        room.TonerData.ItemId = item.Id;
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7, Username = "owner", CurrentRoom = room });
        return (room, client, item);
    }

    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class Store(Action? before = null) : IFurnitureUseStore
    {
        public bool Fail { get; init; }
        public List<(uint, uint, bool)> Values { get; } = [];
        public void SetTonerEnabled(uint itemId, uint roomId, bool enabled)
        {
            before?.Invoke(); Values.Add((itemId, roomId, enabled));
            if (Fail) throw new InvalidOperationException("forced write failure");
        }
    }
}
