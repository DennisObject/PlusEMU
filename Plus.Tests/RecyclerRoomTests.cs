using System.Collections.Immutable;
using System.Data.Common;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Recycler;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData("private")]
    [InlineData("public")]
    public void RecyclerCommitsBeforeAnyLiveRemovalAndDeliversAnUnopenedEcotronBox(string roomType)
    {
        var (service, store, inputs, _) = RecyclerFixture();
        _room.Type = roomType;
        store.BeforeCommit = () =>
        {
            Assert.Empty(_client.Packets);
            Assert.All(inputs, item =>
            {
                Assert.Same(item, Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(item.Id));
                Assert.False(item.TryReserve());
            });
        };
        service.Recycle(_client, [21, 20]);
        Assert.Equal(new uint[] { 20, 21 }, store.Consumed);
        var box = Assert.Single(Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItems);
        Assert.Equal((99u, "ecotron_box", "3-2-2040"), (box.Id, box.Definition.ItemName, box.ExtraData.Serialize()));
        Assert.Equal(new[] { ServerPacketHeader.FurniListRemoveComposer, ServerPacketHeader.FurniListRemoveComposer,
            ServerPacketHeader.FurniListAddComposer, ServerPacketHeader.FurniListNotificationComposer,
            ServerPacketHeader.RecyclerFinishedComposer, ServerPacketHeader.FurniListUpdateComposer }, _client.Sent);
        var result = new FlashIncomingPacket { Buffer = _client.Packets.Single(packet => packet.Header == ServerPacketHeader.RecyclerFinishedComposer).Body };
        Assert.Equal(1, result.ReadInt());
        Assert.Equal(0, result.ReadInt());
        Assert.False(result.HasDataRemaining());
        Assert.All(inputs, item => { Assert.True(item.TryReserve()); item.ReleaseReservation(); });
    }

    [Fact]
    public void RecyclerRefusesBusyDuplicateForeignPlacedTradeAndClosedInputsWithoutReleasingOthersReservations()
    {
        var (service, store, inputs, _) = RecyclerFixture();
        Assert.True(inputs[1].TryReserve());
        service.Recycle(_client, [20, 21]);
        Assert.True(inputs[0].TryReserve());
        inputs[0].ReleaseReservation();
        Assert.False(inputs[1].TryReserve());
        inputs[1].ReleaseReservation();
        service.Recycle(_client, [20, 20]);
        inputs[1].OwnerId = 8;
        service.Recycle(_client, [20, 21]);
        inputs[1].OwnerId = 7;
        inputs[1].UniqueNumber = 1;
        service.Recycle(_client, [20, 21]);
        inputs[1].UniqueNumber = 0;
        inputs[1].Definition.AllowEcotronRecycle = false;
        service.Recycle(_client, [20, 21]);
        inputs[1].Definition.AllowEcotronRecycle = true;
        var actor = _room.GetRoomUserManager().GetRoomUserByHabbo(7)!;
        actor.IsTrading = true;
        service.Recycle(_client, [20, 21]);
        actor.IsTrading = false;
        var habbo = _client.GetHabbo();
        typeof(Plus.HabboHotel.Users.Habbo).GetField("_habboSaved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(habbo, true);
        service.Recycle(_client, [20, 21]);
        typeof(Plus.HabboHotel.Users.Habbo).GetField("_habboSaved", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(habbo, false);
        habbo.CurrentRoom = null;
        service.Recycle(_client, [20, 21]);
        Assert.Empty(store.Consumed);
        Assert.Equal(2, Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItems.Count());
        Assert.All(_client.Packets, packet => Assert.Equal(ServerPacketHeader.RecyclerFinishedComposer, packet.Header));
    }

    [Fact]
    public void RecyclerMissingPrizeDataAndFailedWritesNeverConsumeInputsOrFabricateAReward()
    {
        var (service, store, inputs, _) = RecyclerFixture();
        store.Configuration = store.Configuration with { Levels = [] };
        service.GetStatus(_client);
        service.GetPrizes(_client);
        service.Recycle(_client, [20, 21]);
        Assert.Empty(store.Consumed);
        Assert.Equal(2, new FlashIncomingPacket { Buffer = _client.Packets[0].Body }.ReadInt());
        Assert.Equal(0, new FlashIncomingPacket { Buffer = _client.Packets[1].Body }.ReadInt());
        store.Configuration = new(true, 2, 30, [new(1, 1, [new(1, 102)])]);
        store.BeforeCommit = () => throw new RecyclerFailure();
        service.Recycle(_client, [20, 21]);
        Assert.Empty(store.Consumed);
        Assert.Equal(2, Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItems.Count());
        Assert.All(inputs, item => { Assert.True(item.TryReserve()); item.ReleaseReservation(); });
    }

    [Fact]
    public void RecyclerCooldownUsesCeilingAtTheCapturedUtcBoundary()
    {
        var (service, store, _, _) = RecyclerFixture();
        store.Next = _interactionClock.GetUtcNow().AddMilliseconds(1001);
        service.GetStatus(_client);
        var packet = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
        Assert.Equal(3, packet.ReadInt());
        Assert.Equal(2, packet.ReadInt());
        _client.Packets.Clear();
        store.Next = _interactionClock.GetUtcNow();
        service.GetStatus(_client);
        packet = new FlashIncomingPacket { Buffer = Assert.Single(_client.Packets).Body };
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(0, packet.ReadInt());
    }

    [Theory]
    [InlineData(false, false, "private")]
    [InlineData(false, true, "private")]
    [InlineData(true, false, "private")]
    [InlineData(true, true, "private")]
    [InlineData(false, false, "public")]
    [InlineData(false, true, "public")]
    [InlineData(true, false, "public")]
    [InlineData(true, true, "public")]
    public void OwnedEcotronOpeningDeliversContentsToInventoryAndRemovesOnlyTheCapturedRoomItem(bool v2, bool wallReward, string roomType)
    {
        var (service, store, _, box) = RecyclerFixture(v2, wallReward);
        _room.Type = roomType;

        if (roomType == "public") {
            Assert.False(_room.CheckRights(_client, false, true));
            _client.GetHabbo().Access = Plus.HabboHotel.Permissions.UserAccess.Create([],
                [new(Plus.HabboHotel.Permissions.PermissionKeys.RoomOwnerAny, false)]);
        }

        Assert.True(_room.CheckRights(_client, false, true));
        store.BeforeOpen = () =>
        {
            Assert.Same(box, _room.GetRoomItemHandler().GetItem(box.Id));
            Assert.Null(Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(100));
            Assert.Empty(_client.Packets);
        };
        Assert.True(service.TryOpen(_client, box.Id));

        if (v2) {
            Assert.Equal(0, store.Opens);
            ExecutorTick();
        }

        Assert.Equal(1, store.Opens);
        Assert.Null(_room.GetRoomItemHandler().GetItem(box.Id));
        Assert.Null(box.GetRoom());
        var reward = Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(100)!;
        Assert.Equal((102u, "blue"), (reward.Definition.Id, reward.ExtraData.Serialize()));
        var packet = new FlashIncomingPacket { Buffer = _client.Packets.Single(packet => packet.Header == ServerPacketHeader.OpenGiftComposer).Body };
        Assert.Equal(wallReward ? "i" : "s", packet.ReadString());
        Assert.Equal(42, packet.ReadInt());
        Assert.Equal("reward", packet.ReadString());
        Assert.Equal(100u, packet.ReadUInt());
        Assert.Equal(wallReward ? "i" : "s", packet.ReadString());
        Assert.False(packet.ReadBool());
        Assert.Equal("blue", packet.ReadString());
        Assert.False(packet.HasDataRemaining());
        Assert.False(service.TryOpen(_client, box.Id));
        Assert.Equal(1, store.Opens);
    }

    [Fact]
    public void QueuedEcotronOpenIsDroppedWhenItsActorDepartsBeforeTheRealOwnerTick()
    {
        var (service, store, _, box) = RecyclerFixture(true);
        Assert.True(service.TryOpen(_client, box.Id));
        _room.GetRoomUserManager().RemoveUserFromRoom(_client, false);
        ExecutorTick();
        Assert.Equal(0, store.Opens);
        Assert.Same(box, _room.GetRoomItemHandler().GetItem(box.Id));
        Assert.DoesNotContain(_client.Packets, packet => packet.Header == ServerPacketHeader.OpenGiftComposer);
    }

    [Fact]
    public void FailedForeignAndMalformedEcotronOpeningKeepsRoomAndInventoryUnchanged()
    {
        var (service, store, _, box) = RecyclerFixture();
        box.OwnerId = 8;
        Assert.True(service.TryOpen(_client, box.Id));
        Assert.Equal(0, store.Opens);
        box.OwnerId = 7;
        box.LegacyDataString = "invalid";
        Assert.True(service.TryOpen(_client, box.Id));
        Assert.Equal(0, store.Opens);
        box.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "3-2-2040" };
        store.Content = null;
        Assert.True(service.TryOpen(_client, box.Id));
        store.Content = new(102, "blue");
        store.BeforeOpen = () => throw new RecyclerFailure();
        Assert.True(service.TryOpen(_client, box.Id));
        Assert.Equal(0, store.Opens);
        Assert.Same(box, _room.GetRoomItemHandler().GetItem(box.Id));
        Assert.Null(Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(100));
        Assert.Empty(_client.Packets);
    }

    [Fact]
    public void ExactItemRemovalRefusesAForeignSameIdInstanceWithoutPacketsOrMapChanges()
    {
        var (_, _, _, box) = RecyclerFixture();
        var foreign = Furni(box.Id, InteractionType.None, WiredBoxType.None);
        Assert.False(_room.GetRoomItemHandler().RemoveFurniture(_client, foreign));
        Assert.Same(box, _room.GetRoomItemHandler().GetItem(box.Id));
        Assert.Same(_room, box.GetRoom());
        Assert.Empty(_client.Packets);
    }

    [Fact]
    public void RecyclerMutationsAreCompleteBeforeASendCanDetachTheSession()
    {
        var (service, _, _, _) = RecyclerFixture();
        var habbo = _client.GetHabbo();
        _client.BeforeCapture = header =>
        {
            if (header == ServerPacketHeader.FurniListRemoveComposer) {
                Assert.Equal(99u, Assert.Single(Assert.IsType<InventoryComponent>(habbo.Inventory).Furniture.GetItems).Id);
                typeof(GameClient).GetField("_habbo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(_client, null);
            }
        };
        service.Recycle(_client, [20, 21]);
        Assert.Null(_client.GetHabbo());
        Assert.Equal(99u, Assert.Single(Assert.IsType<InventoryComponent>(habbo.Inventory).Furniture.GetItems).Id);
    }

    [Fact]
    public void RecyclerRefusesAStaleSameHabboActorThatBelongsToAnotherSession()
    {
        var (service, store, _, box) = RecyclerFixture();
        var actor = _room.GetRoomUserManager().GetRoomUserByHabbo(7)!;
        typeof(Plus.HabboHotel.Rooms.RoomUser).GetField("_mClient", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(actor, new TestClient());
        service.Recycle(_client, [20, 21]);
        Assert.True(service.TryOpen(_client, box.Id));
        Assert.Empty(store.Consumed);
        Assert.Equal(0, store.Opens);
        Assert.Same(box, _room.GetRoomItemHandler().GetItem(box.Id));
        Assert.Equal(2, Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItems.Count());
    }

    [Fact]
    public void ExactRemovalKeepsASameIdReplacementAndItsQueuedWriteWhenTheRemovalPacketReentersPlacement()
    {
        var (_, _, _, box) = RecyclerFixture();
        var handler = _room.GetRoomItemHandler();
        handler.RemoveItem(box); // A loaded, already-persisted box has no pending placement write.
        var replacement = Furni(box.Id, InteractionType.None, WiredBoxType.None);
        replacement.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "replacement" };
        IReadOnlyList<Plus.HabboHotel.Rooms.RoomItemSave>? saved = null;
        var recording = Proxy<Plus.HabboHotel.Rooms.IRoomItemStore>((method, args) =>
        {
            if (method == "SaveMoved") {
                saved = (IReadOnlyList<Plus.HabboHotel.Rooms.RoomItemSave>)args[0]!;
            }

            return null;
        });
        typeof(Plus.HabboHotel.Rooms.RoomItemHandling).GetField("_store", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(handler, recording);
        _client.BeforeCapture = header =>
        {
            if (header == ServerPacketHeader.ObjectRemoveComposer) {
                Assert.True(handler.SetFloorItem(null!, replacement, 3, 1, 0, true, false, false));
                handler.UpdateItem(replacement);
            }
        };
        Assert.True(handler.RemoveFurniture(_client, box));
        Assert.Same(replacement, handler.GetItem(box.Id));
        Assert.Same(_room, replacement.GetRoom());
        handler.Dispose();
        var write = Assert.Single(saved!);
        Assert.Equal((box.Id, 3, 1, "replacement"), (write.Id, write.X, write.Y, write.ExtraData));
    }

    [Fact]
    public void AmbiguousEcotronDefinitionsDisableRecyclerWithoutConsumingFurniture()
    {
        var (service, store, _, _) = RecyclerFixture(ambiguousBox: true);
        service.GetStatus(_client);
        service.GetPrizes(_client);
        service.Recycle(_client, [20, 21]);
        Assert.Empty(store.Consumed);
        Assert.Equal(2, new FlashIncomingPacket { Buffer = _client.Packets[0].Body }.ReadInt());
        Assert.Equal(0, new FlashIncomingPacket { Buffer = _client.Packets[1].Body }.ReadInt());
        Assert.Equal(2, Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItems.Count());
    }

    [Fact]
    public void CachedGroupDecorationAdministratorMayOpenTheirOwnedEcotronWithoutOrdinaryRoomRights()
    {
        var (service, store, _, box) = RecyclerFixture();
        _room.OwnerName = "other-owner";
        _room.OwnerId = 8;
        _room.UsersWithRights = [];
        _room.Group = new Plus.HabboHotel.Groups.Group(9, "group", "", "badge", 42, 8, null, 0, 1, 1, 0, false,
            new Plus.HabboHotel.Groups.GroupMembershipSnapshot([], [7], []));
        Assert.False(_room.CheckRights(_client));
        Assert.True(_room.CheckRights(_client, false, true));
        Assert.True(service.TryOpen(_client, box.Id));
        Assert.Equal(1, store.Opens);
        Assert.Null(_room.GetRoomItemHandler().GetItem(box.Id));
        Assert.NotNull(Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Furniture.GetItem(100));
    }

    private (RecyclerService Service, RecyclerRecordingStore Store, InventoryItem[] Inputs, Item Box) RecyclerFixture(bool v2 = false, bool wallReward = false, bool ambiguousBox = false)
    {
        var habbo = _client.GetHabbo();
        habbo.Client = _client;
        habbo.Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []) };

        if (v2) {
            ExecutorActor(0, 1);
        }
        else {
            Assert.True(_room.GetRoomUserManager().AddAvatarToRoom(_client));
        }

        var box = Furni(10, InteractionType.None, WiredBoxType.None);
        box.Definition.Id = 100;
        box.Definition.ItemName = "ecotron_box";
        box.Definition.SpriteId = 3095;
        box.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "3-2-2040" };
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, box, 2, 2, 0, true, false, false));
        var input = new ItemDefinition { Id = 101, ItemName = "input", SpriteId = 41, AllowEcotronRecycle = true };
        var output = new ItemDefinition
        {
            Id = 102,
            ItemName = "reward",
            SpriteId = 42,
            ProductType = wallReward ? "i" : "s",
            Type = wallReward ? ItemType.Wall : ItemType.Floor
        };
        var inputs = new[] { new InventoryItem { Id = 20, OwnerId = 7, Definition = input }, new InventoryItem { Id = 21, OwnerId = 7, Definition = input } };

        foreach (var item in inputs) {
            Assert.True(habbo.Inventory.Furniture.AddItem(item));
        }

        var store = new RecyclerRecordingStore();
        var data = new RecyclerDefinitions([box.Definition, input, output]);

        if (ambiguousBox) {
            data.Items.Add(103, new ItemDefinition { Id = 103, ItemName = "ecotron_box", SpriteId = 3095 });
        }

        var service = new RecyclerService(store, data, _interactionClock, new RecyclerTestRandom([]), TestLogging.For<RecyclerService>());
        _client.Sent.Clear();
        _client.Packets.Clear();

        return (service, store, inputs, box);
    }

    private sealed class RecyclerRecordingStore : IRecyclerStore
    {
        public RecyclerConfiguration Configuration { get; set; } = new(true, 2, 30, [new(1, 1, [new(1, 102)])]);
        public DateTimeOffset? Next { get; set; }
        public Action? BeforeCommit { get; set; }
        public Action? BeforeOpen { get; set; }
        public GiftContent? Content { get; set; } = new(102, "blue");
        public uint[] Consumed { get; private set; } = [];
        public int Opens { get; private set; }
        public RecyclerConfiguration Load() => Configuration;
        public DateTimeOffset? NextAllowed(int userId) => Next;
        public RecycledBox? Recycle(int userId, uint boxDefinitionId, RecyclerConfiguration configuration, RecyclerPrize prize, IReadOnlyList<RecyclerInput> inputs, DateTimeOffset now)
        {
            Assert.Equal((7, 100u), (userId, boxDefinitionId));
            BeforeCommit?.Invoke();
            Consumed = inputs.Select(input => input.Id).ToArray();

            return new(99, "3-2-2040");
        }
        public GiftContent? FindBox(uint itemId, int ownerId, uint roomId, uint boxDefinitionId) => Content;
        public uint? OpenBox(uint itemId, int ownerId, uint roomId, uint boxDefinitionId, GiftContent content)
        {
            Assert.Equal((10u, 7, 42u, 100u), (itemId, ownerId, roomId, boxDefinitionId));
            BeforeOpen?.Invoke();
            Opens++;

            return 100;
        }
    }
    private sealed class RecyclerFailure() : DbException("forced recycler failure");
    private sealed class RecyclerDefinitions(IEnumerable<ItemDefinition> definitions) : IItemDataManager
    {
        public Dictionary<uint, ItemDefinition> Items { get; } = definitions.ToDictionary(definition => definition.Id);
        public Dictionary<int, uint> Gifts { get; } = [];
        public void Init() => throw new NotSupportedException();
        public ItemDefinition? GetItemByName(string name) => Items.Values.SingleOrDefault(definition => definition.ItemName == name);
    }
}

internal sealed class RecyclerTestRandom(IEnumerable<int> draws) : IRecyclerRandom
{
    private readonly Queue<int> _draws = new(draws);
    public List<int> Bounds { get; } = [];
    public int Next(int maximum)
    {
        Bounds.Add(maximum);
        var draw = _draws.Count == 0 ? 0 : _draws.Dequeue();
        Assert.InRange(draw, 0, maximum - 1);

        return draw;
    }
}
