using Plus.Communication.Packets.Outgoing.Inventory.Trading;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Trading;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class TradeOfferSnapshotTests
{
    [Fact]
    public void FloorLtdWallAndCurrencyFieldsRetainTheirOrderAndSnapshotSourceState()
    {
        var first = new RoomUser(7, 42, 1, null!, null) { UserId = 7 };
        var second = new RoomUser(8, 42, 2, null!, null) { UserId = 8 };
        var trade = new Trade(1, first, second, null!, null!);
        var floor = new InventoryItem
        {
            Id = 100,
            Definition = new ItemDefinition { Type = ItemType.Floor, SpriteId = 11, InteractionType = InteractionType.Exchange, BehaviourData = 10 },
            UniqueNumber = 2,
            UniqueSeries = 20
        };
        var wall = new InventoryItem { Id = 101, Definition = new ItemDefinition { Type = ItemType.Wall, SpriteId = 12 } };
        trade.Users[0].OfferedItems.Add(floor.Id, floor);
        trade.Users[0].OfferedItems.Add(wall.Id, wall);
        var composer = new TradingUpdateComposer(TradeOfferSnapshot.Capture(trade));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        Assert.Equal(new object[]
        {
            7, 2, 100u, "floor", 100u, 11, 0, false, 256, "", 2u, 20u, 0, 0, 0, 0,
            101u, "wall", 101u, 12, 0, true, 0, "", 0, 0, 0, 2, 10,
            8, 0, 0, 0
        }, before.Writes);

        floor.Definition.SpriteId = 99;
        floor.Definition.BehaviourData = 99;
        floor.UniqueNumber = 0;
        first.UserId = 99;
        trade.Users[0].OfferedItems.Clear();
        trade.Users = [];
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }
}
