using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.FloorPlan;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.HabboHotel.Housekeeping;
using Plus.HabboHotel.Items.Editor;
using Xunit;

namespace Plus.Tests;

public sealed class ScalarCollectionFreezeTests
{
    [Fact]
    public void ScalarCollectionsCannotChangeAfterComposerConstruction()
    {
        Verify(new List<string> { "first" }, values => new FurniEditorInteractionsResultComposer(values), [1, "first"]);
        Verify(new List<RoomRightHolder> { new(7, "owner") }, values => new RoomRightsListComposer(42, values), [42u, 1, 7, "owner"]);
        Verify(new List<GroupFurniConfig> { new(9, "group", "badge", "AA", "BB", 7, true) },
            values => new GroupFurniConfigComposer(values), [1, 9, "group", "badge", "AA", "BB", false, 7, true]);
        Verify(new List<(int X, int Y)> { (2, 3) }, values => new RoomOccupiedTilesComposer(values), [1, 2, 3]);
        Verify(new List<KeyValuePair<string, int>> { new("tag", 5) }, values => new PopularRoomTagsResultComposer(values), [1, "tag", 5]);
        Verify(new List<(string EntityType, int EntityId)> { ("page", 7) },
            values => new CatalogStudioOperationComposer("op", true, "ok", "done", 2, values),
            ["op", true, "ok", "done", 2, 1, "page", 7]);
        Verify(new List<FurnidataEdit> { new("old", "new", false, 7, "chair", "Chair", "Description") },
            values => new FurnitureDataReloadComposer(FurnitureDataReloadComposer.Delta, values),
            [0, 1, "S", 7, "chair", "Chair", "Description"]);
        Verify(new List<FloorHeightMapComposer.AreaHide> { new(7, true, 2, 3, 4, 5, false) },
            values => new FloorHeightMapComposer("map", 2, true, values, 6, 7, 1.5f),
            [true, 2, "map", 1, 7, true, 2, 3, 4, 5, false, 6, 7, BitConverter.SingleToInt32Bits(1.5f)]);
    }

    [Fact]
    public void FavouriteIdsAreFrozenAtConstruction()
    {
        var ids = new System.Collections.ArrayList { 7, 42 };
        var composer = new FavouritesComposer(ids);
        ids[0] = 99;
        ids.Clear();
        Recompose(composer, new object[] { 50, 2, 7, 42 });
    }

    [Fact]
    public void HousekeepingRoomListIsFrozenWhileKeepingRoomFieldOrder()
    {
        var room = new HousekeepingRoom(42, "room", "description", 7, "owner", 3, 25, true, false, false, 123);
        var values = new List<HousekeepingRoom> { room };
        var composer = new HousekeepingRoomListComposer(values);
        var expected = new HabbiconTestSupport.RecordingPacket();
        expected.WriteInteger(1);
        HousekeepingRoomDetailComposer.WriteRoom(expected, room);
        values[0] = room with
        {
            Name = "changed",
            UserCount = 0
        };
        values.Clear();
        Recompose(composer, expected.Writes);
    }

    private static void Verify<T>(List<T> source, Func<List<T>, IServerPacket> create, object[] expected)
    {
        var composer = create(source);
        source.Clear();
        Recompose(composer, expected);
    }

    private static void Recompose(IServerPacket composer, IEnumerable<object> expected)
    {
        for (var index = 0; index < 2; index++)
        {
            var packet = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(packet);
            Assert.Equal(expected, packet.Writes);
        }
    }
}
