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
    public async Task FigureHandlerFullyDecodesBeforeDelegatingWithoutRoomState()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var metadata = new RecordingMetadata();
        var packet = HabbiconTestSupport.Incoming(-2);
        await new Plus.Communication.Packets.Incoming.Rooms.Furni.SetMannequinFigureEvent(metadata).Parse(client, packet);
        Assert.Same(client, metadata.Session);
        Assert.Equal(uint.MaxValue - 1, metadata.ItemId);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(sent);
    }

    private sealed class RecordingMetadata : IRoomItemMetadataService
    {
        public GameClient? Session;
        public uint ItemId;
        public void SetMannequinFigure(GameClient session, uint itemId) => (Session, ItemId) = (session, itemId);
        public void SetMannequinName(GameClient session, MannequinNameRequest request) => throw new NotSupportedException();
        public void SetToner(Room room, GameClient session, TonerSettingsRequest request) => throw new NotSupportedException();
        public void SetBranding(GameClient session, BrandingRequest request) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MannequinFigureIsPreparedAndPersistedBeforeStatePublication(bool fail)
    {
        var (room, client) = Context();
        var item = AddItem(room, InteractionType.Mannequin);
        var original = $"m{(char)5}old{(char)5}display name";
        item.LegacyDataString = original;
        client.GetHabbo().Gender = "F";
        client.GetHabbo().Look = "hd-1.hr-2.ch-3.lg-4.ea-5.";
        client.GetHabbo().Clothing = new();
        var store = new RecordingStore(() => Assert.Equal(original, item.LegacyDataString)) { Fail = fail };
        var service = new RoomItemMetadataService(store, Figures());
        if (fail)
        {
            Assert.Throws<InvalidOperationException>(() => service.SetMannequinFigure(client, item.Id));
            Assert.Equal(original, item.LegacyDataString);
        }
        else
        {
            service.SetMannequinFigure(client, item.Id);
            Assert.Equal($"f{(char)5}ch-3.lg-4{(char)5}display name", item.LegacyDataString);
        }
        Assert.Equal(1, store.Writes);
        Assert.Equal($"f{(char)5}ch-3.lg-4{(char)5}display name", store.Data);
    }

    [Theory]
    [InlineData(InteractionType.Background)]
    [InlineData(InteractionType.Gate)]
    public void FigureUpdateRequiresExactMannequinType(InteractionType type)
    {
        var (room, client) = Context();
        var item = AddItem(room, type);
        var store = new RecordingStore();
        new RoomItemMetadataService(store, null!).SetMannequinFigure(client, item.Id);
        Assert.Equal(0, store.Writes);
        Assert.Equal("original", item.LegacyDataString);
    }

    [Fact]
    public void IncompleteMannequinNameFieldsAreANoOpBeforeFigurePreparation()
    {
        var (room, client) = Context();
        var item = AddItem(room, InteractionType.Mannequin);
        item.LegacyDataString = $"m{(char)5}figure";
        var store = new RecordingStore();
        new RoomItemMetadataService(store, null!).SetMannequinFigure(client, item.Id);
        Assert.Equal(0, store.Writes);
        Assert.Equal($"m{(char)5}figure", item.LegacyDataString);
    }

    private static Plus.Core.FigureData.IFigureDataManager Figures()
    {
        var figures = DispatchProxy.Create<Plus.Core.FigureData.IFigureDataManager, FigureProxy>();
        return figures;
    }

    public class FigureProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("ProcessFigure", method!.Name);
            return args![0];
        }
    }

    [Fact]
    public void WrongItemTypeAndOutOfRangeTonerValuesAreRejected()
    {
        var (room, client) = Context();
        var item = AddItem(room, InteractionType.Gate);
        room.TonerData = Toner(item.Id);
        var store = new RecordingStore();
        var service = new RoomItemMetadataService(store, null!);

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

        new RoomItemMetadataService(store, null!).SetMannequinName(client, new(item.Id, "changed"));

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

        Assert.Throws<InvalidOperationException>(() => new RoomItemMetadataService(store, null!).SetMannequinName(client, new(item.Id, "new")));

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

        new RoomItemMetadataService(store, null!).SetToner(room, client, new(item.Id, 0, 255, 1));

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

        Assert.Throws<InvalidOperationException>(() => new RoomItemMetadataService(store, null!).SetToner(room, client, new(item.Id, 40, 50, 60)));

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
        public string? Data;
        public void SetMannequinData(uint itemId, uint roomId, string data) { Data = data; Write(); }
        public void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness) => Write();
        public void SetBrandingData(uint itemId, uint roomId, string data) { Data = data; Write(); }
        private void Write() { beforeWrite?.Invoke(); Writes++; if (Fail) throw new InvalidOperationException("forced failure"); }
    }
}
