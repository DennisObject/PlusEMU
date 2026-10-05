using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Data.Toner;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RoomItemMetadataServiceTests
{
    [Fact]
    public void WrongItemTypeAndOutOfRangeTonerValuesAreRejected()
    {
        var (room, client) = Context();
        var item = AddItem(room, InteractionType.Gate);
        room.TonerData = Toner(item.Id);
        var store = new RecordingStore();
        var service = new RoomItemMetadataService(store);

        service.SetMannequinName(client, new(item.Id, "changed"));
        service.SetToner(room, client, new(item.Id, -1, 20, 30));
        service.SetToner(room, client, new(item.Id, 20, 256, 30));

        Assert.Equal(0, store.Writes);
        Assert.Equal("original", item.LegacyDataString);
        Assert.Equal(0, room.TonerData.Enabled);
    }

    [Fact]
    public void UserWithoutOwnerRightsCannotChangeMetadata()
    {
        var (room, client) = Context();
        room.OwnerName = "someone-else";
        var item = AddItem(room, InteractionType.Mannequin);
        var store = new RecordingStore();

        new RoomItemMetadataService(store).SetMannequinName(client, new(item.Id, "changed"));

        Assert.Equal(0, store.Writes);
        Assert.Equal("original", item.LegacyDataString);
    }

    [Fact]
    public void MannequinPersistencePrecedesMemoryPublicationAndFailureLeavesStateUntouched()
    {
        var (room, client) = Context();
        var item = AddItem(room, InteractionType.Mannequin);
        item.LegacyDataString = $"f{(char)5}figure{(char)5}old";
        var store = new RecordingStore(() => Assert.EndsWith($"{(char)5}old", item.LegacyDataString)) { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new RoomItemMetadataService(store).SetMannequinName(client, new(item.Id, "new")));

        Assert.Equal($"f{(char)5}figure{(char)5}old", item.LegacyDataString);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public void ValidTonerPersistencePrecedesStateAndAcceptsChannelBoundaries()
    {
        var (room, client) = Context();
        var item = AddItem(room, InteractionType.Toner);
        room.TonerData = Toner(item.Id);
        var store = new RecordingStore(() =>
        {
            Assert.Equal(0, room.TonerData.Enabled);
            Assert.Equal(0, room.TonerData.Hue);
        });

        new RoomItemMetadataService(store).SetToner(room, client, new(item.Id, 0, 255, 1));

        Assert.Equal(1, store.Writes);
        Assert.Equal((0, 255, 1, 1), (room.TonerData.Hue, room.TonerData.Saturation, room.TonerData.Lightness, room.TonerData.Enabled));
    }

    [Fact]
    public void TonerPersistenceFailureLeavesMemoryUntouched()
    {
        var (room, client) = Context();
        var item = AddItem(room, InteractionType.Toner);
        room.TonerData = Toner(item.Id);
        room.TonerData.Hue = 10; room.TonerData.Saturation = 20; room.TonerData.Lightness = 30;
        var store = new RecordingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new RoomItemMetadataService(store).SetToner(room, client, new(item.Id, 40, 50, 60)));

        Assert.Equal((10, 20, 30, 0), (room.TonerData.Hue, room.TonerData.Saturation, room.TonerData.Lightness, room.TonerData.Enabled));
    }

    private static (Room Room, GameClient Client) Context()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9; room.OwnerName = "owner"; room.Type = "private"; room.UsersWithRights = [];
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "owner", CurrentRoom = room });
        return (room, client);
    }

    private static Item AddItem(Room room, InteractionType type)
    {
        var item = new Item { Id = 7, RoomId = room.Id, OwnerId = 1, Definition = new() { InteractionType = type }, ExtraData = new LegacyDataFormat { Data = "original" } };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, room);
        floor[item.Id] = item;
        return item;
    }

    private static TonerData Toner(uint itemId)
    {
        var data = (TonerData)RuntimeHelpers.GetUninitializedObject(typeof(TonerData));
        data.ItemId = itemId;
        return data;
    }

    private sealed class RecordingStore(Action? beforeWrite = null) : IRoomItemMetadataStore
    {
        public bool Fail { get; init; }
        public int Writes { get; private set; }
        public void SetMannequinData(uint itemId, uint roomId, string data) => Write();
        public void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness) => Write();
        private void Write() { beforeWrite?.Invoke(); Writes++; if (Fail) throw new InvalidOperationException("forced failure"); }
    }
}
