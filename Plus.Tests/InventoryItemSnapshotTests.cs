using System.Buffers.Binary;
using System.Collections.Immutable;
using Microsoft.IO;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Incoming.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class InventoryItemSnapshotTests
{
    [Theory]
    [MemberData(nameof(RoomItemSnapshotTests.DataCases), MemberType = typeof(RoomItemSnapshotTests))]
    public void InventoryListAndAddPreserveEverySupportedDataFormat(FurniObjectData data, object[] dataFields)
    {
        var snapshot = InventoryItemSnapshot.Capture(Item(17, data));
        var fields = new object[] { 17u, "S", 17u, 31, 1 }.Concat(dataFields)
            .Concat(new object[] { true, true, true, true, -1, false, -1, "", 0 }).ToArray();
        Assert.Equal(Encode(fields), Encode(new FurniListAddComposer(snapshot)));
        Assert.Equal(Encode(new object[] { 1, 0, 1 }.Concat(fields)), Encode(new FurniListComposer([snapshot], 1, 0)));
    }

    [Fact]
    public void LimitedWallItemSnapshotCopiesNestedDataDefinitionAndCollection()
    {
        var data = new MapDataFormat(new() { ["state"] = "before" });
        var item = Item(17, data);
        item.Definition.Type = ItemType.Wall;
        item.UniqueNumber = 4; item.UniqueSeries = 20;
        var source = new[] { InventoryItemSnapshot.Capture(item) };
        var composer = new FurniListComposer(source.ToImmutableArray(), 2, 0);
        var addition = new FurniListAddComposer(source[0]);
        var fields = new object[] { 17u, "I", 17u, 31, 1, 0xFF01, 1, "state", "before", 4u, 20u,
            true, true, false, true, -1, false, -1 };
        var expectedList = Encode(new object[] { 2, 0, 1 }.Concat(fields));
        var expectedAdd = Encode(fields);
        data.Data["state"] = "after";
        data.Data["extra"] = "changed";
        item.Definition.SpriteId = 99; item.Definition.AllowTrade = false;
        item.UniqueNumber = 100; item.UniqueSeries = 0;
        source[0] = InventoryItemSnapshot.Capture(Item(99, new EmptyDataFormat()));
        Assert.Equal(expectedList, Encode(composer));
        Assert.Equal(expectedList, Encode(composer));
        Assert.Equal(expectedAdd, Encode(addition));
    }

    [Fact]
    public void ListAndAdditionKeepTheirExistingDistinctStackingFlags()
    {
        var item = Item(17, new EmptyDataFormat());
        item.UniqueNumber = 0; item.UniqueSeries = 20;
        var snapshot = InventoryItemSnapshot.Capture(item);
        var list = new HabbiconTestSupport.RecordingPacket();
        var addition = new HabbiconTestSupport.RecordingPacket();
        new FurniListComposer([snapshot], 1, 0).Compose(list);
        new FurniListAddComposer(snapshot).Compose(addition);
        Assert.Equal(new object[] { true, true, false, true, false }, list.Writes.Where(value => value is bool));
        Assert.Equal(new object[] { true, true, true, true, false }, addition.Writes.Where(value => value is bool));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(700, 1)]
    [InlineData(701, 2)]
    [InlineData(1400, 2)]
    public async Task RequestInventoryPreservesPageBoundariesAndEmptyPageIndex(int count, int pages)
    {
        var items = Enumerable.Range(1, count).Select(id => Item((uint)id, new EmptyDataFormat())).ToArray();
        var habbo = new Habbo { Inventory = new() { Furniture = new(items, []) } };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        await new RequestFurniInventoryEvent().Parse(client, HabbiconTestSupport.Incoming());
        Assert.Equal(pages, sent.Count);
        var actualCount = 0;
        for (var index = 0; index < sent.Count; index++)
        {
            var packet = sent[index];
            Assert.Equal(ServerPacketHeader.FurniListComposer, packet.Header);
            Assert.Equal(pages, BinaryPrimitives.ReadInt32BigEndian(packet.Payload));
            Assert.Equal(count == 0 ? 1 : index, BinaryPrimitives.ReadInt32BigEndian(packet.Payload.AsSpan(4)));
            var pageCount = BinaryPrimitives.ReadInt32BigEndian(packet.Payload.AsSpan(8));
            Assert.Equal(Math.Min(700, count - index * 700), pageCount);
            actualCount += pageCount;
        }
        Assert.Equal(count, actualCount);
    }

    private static InventoryItem Item(uint id, FurniObjectData data) => new()
    {
        Id = id, ExtraData = data,
        Definition = new ItemDefinition { SpriteId = 31, Type = ItemType.Floor, Category = FurniCategory.Default,
            AllowEcotronRecycle = true, AllowTrade = true, AllowInventoryStack = true, AllowMarketplaceSell = true }
    };

    private static byte[] Encode(IServerPacket composer)
    {
        using var stream = (RecyclableMemoryStream)new RecyclableMemoryStreamManager().GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));
        return stream.ToArray()[6..];
    }

    private static byte[] Encode(IEnumerable<object> fields)
    {
        using var stream = (RecyclableMemoryStream)new RecyclableMemoryStreamManager().GetStream();
        IOutgoingPacket packet = new FlashOutgoingPacket(stream);
        foreach (var field in fields)
            switch (field)
            {
                case int value: packet.WriteInt(value); break;
                case uint value: packet.WriteUInt(value); break;
                case bool value: packet.WriteBool(value); break;
                case string value: packet.WriteString(value); break;
                default: throw new ArgumentOutOfRangeException(nameof(fields));
            }
        return stream.ToArray()[6..];
    }
}
