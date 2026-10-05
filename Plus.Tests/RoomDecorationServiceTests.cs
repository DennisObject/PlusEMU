using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Rooms.FloorPlan;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class RoomDecorationServiceTests
{
    [Fact]
    public void WrongRoomOwnerItemAndItemTypeAreDeniedWithoutPersistence()
    {
        var (room, client, _) = Context("owner");
        var store = new DecorationStore();
        var service = Service(store);
        var wrongOwner = Inventory(7, 99, InteractionType.Wallpaper, "paper");
        client.GetHabbo().Inventory = InventoryWith(wrongOwner);
        service.Apply(room, client, new(7));
        wrongOwner.OwnerId = 1; wrongOwner.Definition.InteractionType = InteractionType.Gate;
        service.Apply(room, client, new(7));
        client.GetHabbo().CurrentRoom = null;
        wrongOwner.Definition.InteractionType = InteractionType.Wallpaper;
        service.Apply(room, client, new(7));

        Assert.Equal(0, store.Writes);
        Assert.Equal("old", room.Wallpaper);
    }

    [Fact]
    public void PersistenceFailureLeavesInventoryRoomAndPacketsUntouched()
    {
        var (room, client, sent) = Context("owner");
        var item = Inventory(7, 1, InteractionType.Wallpaper, "paper");
        client.GetHabbo().Inventory = InventoryWith(item);
        var store = new DecorationStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => Service(store).Apply(room, client, new(item.Id)));

        Assert.Equal("old", room.Wallpaper);
        Assert.NotNull(client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
        Assert.Empty(sent);
    }

    [Fact]
    public void ValidDecorationPublishesOnlyAfterStoreReturns()
    {
        var (room, client, sent) = Context("owner");
        var item = Inventory(7, 1, InteractionType.Wallpaper, "paper");
        client.GetHabbo().Inventory = InventoryWith(item);
        var store = new DecorationStore(() =>
        {
            Assert.Equal("old", room.Wallpaper);
            Assert.NotNull(client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
            Assert.Empty(sent);
        });

        Service(store).Apply(room, client, new(item.Id));
        Service(store).Apply(room, client, new(item.Id));

        Assert.Equal("paper", room.Wallpaper);
        Assert.Null(client.GetHabbo().Inventory.Furniture.GetItem(item.Id));
        Assert.Single(sent);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task FloorPlanHandlerOnlyDecodesAndDelegates()
    {
        var capture = new FloorCapture();
        var (room, client, _) = Context("owner");
        await new UpdateFloorPropertiesEvent(capture).Parse(room, client, Packet("00\r00", 1, 2, 3, 0, -1, 4));

        Assert.Equal(room, capture.Room);
        Assert.Equal("00\r00", capture.Body.Map);
        Assert.True(capture.Body.DoorFieldsPresent);
        Assert.True(capture.Body.WallHeightPresent);
        Assert.Equal(new FloorPlanSave.Layout(1, 2, 3, 0, -1, 4), capture.Body.Requested);
    }

    [Fact]
    public void FloorPlanServiceDeniesNonOwnerBeforePersistence()
    {
        var (room, client, _) = Context("owner", "visitor");
        var store = new FloorStore();
        var service = new FloorPlanUpdateService(Proxy<IRoomManager>(), store);

        service.Update(room, client, new("00", false, false, default));

        Assert.Equal(0, store.Writes);
    }

    private static RoomDecorationService Service(IRoomDecorationStore store) => new(store, Proxy<IAchievementManager>(), Proxy<IQuestManager>());

    private static (Room Room, GameClient Client, List<(uint Header, byte[] Payload)> Sent) Context(string owner, string username = "owner")
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9; room.OwnerName = owner; room.Type = "private"; room.Wallpaper = "old"; room.UsersWithRights = [];
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = username == owner ? 1 : 2, Username = username, CurrentRoom = room });
        return (room, client, sent);
    }

    private static InventoryItem Inventory(uint id, uint owner, InteractionType type, string data) => new()
    {
        Id = id, OwnerId = owner, Definition = new() { Type = ItemType.Floor, InteractionType = type }, ExtraData = new LegacyDataFormat { Data = data }
    };

    private static InventoryComponent InventoryWith(InventoryItem item) => new() { Furniture = new FurnitureInventoryComponent([item], []) };

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, EmptyProxy>();
    public class EmptyProxy : DispatchProxy { protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.ReturnType == typeof(bool) ? false : null; }

    private static IIncomingPacket Packet(params object[] values)
    {
        using var stream = new MemoryStream();
        foreach (var value in values)
            if (value is string text) { var bytes = Encoding.UTF8.GetBytes(text); stream.WriteByte((byte)(bytes.Length >> 8)); stream.WriteByte((byte)bytes.Length); stream.Write(bytes); }
            else { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, (int)value); stream.Write(bytes); }
        return new Plus.Communication.Flash.FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    private sealed class DecorationStore(Action? before = null) : IRoomDecorationStore
    {
        public int Writes; public bool Fail;
        public void Apply(uint roomId, uint itemId, int userId, RoomDecorationKind kind, string data) { before?.Invoke(); Writes++; if (Fail) throw new InvalidOperationException("forced"); }
    }
    private sealed class FloorStore : IFloorPlanStore { public int Writes; public void Save(uint roomId, string modelName, FloorPlanSave.Decision decision) => Writes++; }
    private sealed class FloorCapture : IFloorPlanUpdateService
    {
        public Room? Room; public FloorPlanUpdateRequest Body;
        public void Update(Room room, GameClient session, FloorPlanUpdateRequest body) { Room = room; Body = body; }
    }
}
