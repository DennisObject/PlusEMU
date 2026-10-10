using Plus.Communication.Packets.Incoming.Recycler;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Recycler;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RecyclerPacketTests
{
    [Fact]
    public void AirV75AndBrowserPrizeStatusAndFinishedContractsContainEveryNativeField()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new RecyclerPrizesComposer([new(1, 1, [new("chair", "s", 42)]), new(2, 4, [new("poster", "i", 7)])]).Compose(packet);
        Assert.Equal(new object[] { 2, 1, 1, 1, "chair", 1, "s", 42, 2, 4, 1, "poster", 1, "i", 7 }, packet.Writes);
        packet = new();
        new RecyclerStatusComposer(3, 41).Compose(packet);
        Assert.Equal(new object[] { 3, 41 }, packet.Writes);
        packet = new();
        new RecyclerFinishedComposer(1, 0).Compose(packet);
        Assert.Equal(new object[] { 1, 0 }, packet.Writes);
        packet = new();
        new RecyclerPrizesComposer([]).Compose(packet);
        Assert.Equal(new object[] { 0 }, packet.Writes);
        packet = new();
        new OpenGiftComposer(new("i", 7, "poster", 99, false, "blue")).Compose(packet);
        Assert.Equal(new object[] { "i", 7, "poster", 99u, "i", false, "blue" }, packet.Writes);
    }

    [Fact]
    public async Task RequestsDelegateAndOrdinaryPresentsRetainTheirOriginalOpeningService()
    {
        var recycler = new Recorder();
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        await new GetRecyclerPrizesEvent(recycler).Parse(client, HabbiconTestSupport.Incoming());
        await new GetRecyclerStatusEvent(recycler).Parse(client, HabbiconTestSupport.Incoming());
        await new RecyclerRecycleEvent(recycler).Parse(client, HabbiconTestSupport.Incoming(2, 20, 21));
        var gifts = new Gifts();
        await new OpenGiftEvent(gifts, recycler).Parse(client, HabbiconTestSupport.Incoming(10));
        Assert.Equal(10u, gifts.Opened);
        recycler.IsBox = true;
        await new OpenGiftEvent(gifts, recycler).Parse(client, HabbiconTestSupport.Incoming(11));
        Assert.Equal(10u, gifts.Opened);
        Assert.Equal(new[] { "prizes", "status", "recycle:20,21", "open:10", "open:11" }, recycler.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(13)]
    [InlineData(int.MaxValue)]
    [InlineData(2)]
    public async Task InvalidAndTruncatedRequestsCannotReadOrAllocateUnboundedInputs(int count)
    {
        var recycler = new Recorder();
        var (client, _) = HabbiconTestSupport.Client(new Habbo());
        await new RecyclerRecycleEvent(recycler).Parse(client, HabbiconTestSupport.Incoming(count));
        Assert.Equal("recycle:", Assert.Single(recycler.Calls));
    }

    [Fact]
    public void EcotronInventoryCategorySurvivesReloadWithoutMutatingSharedFurnitureDefinitions()
    {
        var definition = new ItemDefinition { ItemName = "ecotron_box", SpriteId = 3095, Type = Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Floor };

        foreach (var id in new uint[] { 10, 11 }) {
            var item = new Plus.HabboHotel.Users.Inventory.Furniture.InventoryItem
            {
                Id = id,
                Definition = definition,
                ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "3-2-2040" }
            };
            var snapshot = Plus.HabboHotel.Users.Inventory.Furniture.InventoryItemSnapshot.Capture(item);
            Assert.Equal(Plus.HabboHotel.Users.Inventory.Furniture.FurniCategory.EcotronBox, snapshot.Category);
            var packet = new HabbiconTestSupport.RecordingPacket();
            new Plus.Communication.Packets.Outgoing.Inventory.Furni.FurniListAddComposer(snapshot).Compose(packet);
            Assert.Equal(10, packet.Writes[5]);
        }

        Assert.Equal(Plus.HabboHotel.Users.Inventory.Furniture.FurniCategory.Default, definition.Category);
        definition.SpriteId = 7;
        Assert.Equal(definition.Category, Plus.HabboHotel.Users.Inventory.Furniture.InventoryItemSnapshot.Capture(new() { Id = 12, Definition = definition }).Category);
    }

    [Fact]
    public void PrizeDrawsUseTheReferenceDescendingInverseDenominatorsAndUniformWithinLevel()
    {
        var configuration = new RecyclerConfiguration(true, 5, 0, [new(1, 1, [new(1, 10)]), new(2, 4, [new(2, 20), new(3, 21)]), new(5, 2000, [new(4, 50)])]);
        var random = new RecyclerTestRandom([1999, 0]);
        var service = new RecyclerService(null!, null!, TimeProvider.System, random, TestLogging.For<RecyclerService>());
        Assert.Equal(50u, service.Pick(configuration).ItemId);
        Assert.Equal(new[] { 2000, 1 }, random.Bounds);
        random = new([0, 3, 1]);
        service = new(null!, null!, TimeProvider.System, random, TestLogging.For<RecyclerService>());
        Assert.Equal(21u, service.Pick(configuration).ItemId);
        Assert.Equal(new[] { 2000, 4, 2 }, random.Bounds);
        random = new([0, 0, 0]);
        service = new(null!, null!, TimeProvider.System, random, TestLogging.For<RecyclerService>());
        Assert.Equal(10u, service.Pick(configuration).ItemId);
        Assert.Equal(new[] { 2000, 4, 1 }, random.Bounds);
    }

    private sealed class Recorder : IRecyclerService
    {
        public List<string> Calls { get; } = [];
        public bool IsBox { get; set; }
        public void GetPrizes(GameClient session) => Calls.Add("prizes");
        public void GetStatus(GameClient session) => Calls.Add("status");
        public void Recycle(GameClient session, IReadOnlyList<uint> ids) => Calls.Add("recycle:" + string.Join(',', ids));
        public bool TryOpen(GameClient session, uint id)
        {
            Calls.Add("open:" + id);

            return IsBox;
        }
    }
    private sealed class Gifts : IGiftOpeningService
    {
        public uint Opened { get; private set; }
        public Task OpenAsync(GameClient session, uint id)
        {
            Opened = id;

            return Task.CompletedTask;
        }
    }
}
