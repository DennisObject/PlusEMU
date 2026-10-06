using Microsoft.IO;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class RoomItemSnapshotTests
{
    public static IEnumerable<object[]> DataCases()
    {
        yield return [new EmptyDataFormat(), new object[] { 4 }];
        yield return [new LegacyDataFormat { Data = "on" }, new object[] { 0, "on" }];
        yield return [new MapDataFormat(new() { ["state"] = "on", ["color"] = "blue" }), new object[] { 1, 2, "state", "on", "color", "blue" }];
        yield return [new StringArrayDataFormat { Data = ["one", "two"] }, new object[] { 2, 2, "one", "two" }];
        yield return [new VoteResultDataFormat { State = "open", Result = 7 }, new object[] { 3, "open", 7 }];
        yield return [new IntArrayDataFormat { Data = [2, 9] }, new object[] { 5, 2, 2, 9 }];
        yield return [new HighscoreDataFormat { State = "scores", ScoreType = 2, ClearType = 3 }, new object[] { 6, "scores", 2u, 3u, 0u }];
        yield return [new CrackableDataFormat { State = "hits", Hits = 4, Target = 8 }, new object[] { 7, "hits", 4u, 8u }];
    }

    [Theory]
    [MemberData(nameof(DataCases))]
    public void FloorSnapshotPreservesEverySupportedDataWireFormat(FurniObjectData data, object[] dataFields)
    {
        var item = Item(data);
        var composer = new ObjectUpdateComposer(RoomItemSnapshot.Capture(item));
        var fields = new object[] { 17u, 31, 0, 0, 0, "1.5", "0.25", 1 }
            .Concat(dataFields).Concat(new object[] { -1, 1, 7, 1, 0, 0, 1, 2, 3, 0 }).ToArray();
        Assert.Equal(Encode(fields), Encode(composer));
    }

    [Fact]
    public void NestedDataAndDefinitionChangesCannotChangeAComposedSnapshot()
    {
        var data = new MapDataFormat(new() { ["state"] = "before" });
        var item = Item(data);
        item.UniqueNumber = 4;
        item.UniqueSeries = 20;
        var snapshot = RoomItemSnapshot.Capture(item);
        var composer = new ObjectAddComposer(snapshot);
        var expected = Encode(new object[] { 17u, 31, 0, 0, 0, "1.5", "0.25", 1,
            0xFF01, 1, "state", "before", 4u, 20u, -1, 1, 7, 1, 0, 0, 1, 2, 3, 0, "Alice" });
        data.Data["state"] = "after";
        data.Data["new"] = "entry";
        item.GetZ = 9;
        item.UserId = 99;
        item.Username = "changed";
        item.Definition.SpriteId = 100;
        item.Definition.Stackable = false;
        Assert.Equal(expected, Encode(composer));
        Assert.Equal(expected, Encode(composer));
    }

    [Fact]
    public void WallAndOwnerSnapshotsRemainStableAfterSourceMutation()
    {
        var item = Item(new LegacyDataFormat { Data = "FFFF00 sticky text" });
        item.Definition.Type = ItemType.Wall;
        item.Definition.InteractionType = InteractionType.Postit;
        item.WallCoordinates = ":w=1,1 l=1,1 r";
        var items = new[] { item };
        var furniture = RoomFurnitureSnapshot.Capture(items, 7, "room owner");
        var wall = new ItemAddComposer(furniture.Items[0]);
        var all = new ItemsComposer(furniture);
        var expectedWall = Encode(new object[] { "17", 31, ":w=1,1 l=1,1 r", "FFFF00", -1, 1, 7, 1, 0, 0, 1, 2, 3, 0, "Alice" });
        var expectedAll = Encode(new object[] { 1, 7, "room owner", 1, "17", 31, ":w=1,1 l=1,1 r", "FFFF00", -1, 1, 7, 1, 0, 0, 1, 2, 3, 0 });
        item.LegacyDataString = "changed";
        item.WallCoordinates = "changed";
        item.Username = "changed";
        items[0] = Item(new EmptyDataFormat());
        Assert.Equal(expectedWall, Encode(wall));
        Assert.Equal(expectedWall, Encode(wall));
        Assert.Equal(expectedAll, Encode(all));
    }

    private static Item Item(FurniObjectData data) => new()
    {
        Id = 17,
        UserId = 7,
        Username = "Alice",
        GetZ = 1.5,
        ExtraData = data,
        Definition = new ItemDefinition
        {
            SpriteId = 31,
            Height = 0.25,
            Modes = 2,
            Type = ItemType.Floor,
            Stackable = true,
            Walkable = true,
            Width = 2,
            Length = 3,
            ItemName = "probe",
            PublicName = "probe",
            VendingIds = [],
            AdjustableHeights = []
        }
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

        foreach (var field in fields) {
            switch (field) {
                case int value:
                    packet.WriteInt(value);
                    break;
                case uint value:
                    packet.WriteUInt(value);
                    break;
                case string value:
                    packet.WriteString(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fields));
            }
        }

        return stream.ToArray()[6..];
    }
}
