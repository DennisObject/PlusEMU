using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.FloorPlan;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Xunit;

namespace Plus.Tests;

public sealed class ScalarCollectionFreezeTests
{
    [Fact]
    public void ScalarCollectionsCannotChangeAfterComposerConstruction()
    {
        Verify(new List<RoomRightHolder> { new(7, "owner") }, values => new RoomRightsListComposer(42, values), [42u, 1, 7, "owner"]);
        Verify(new List<GroupFurniConfig> { new(9, "group", "badge", "AA", "BB", 7, true) },
            values => new GroupFurniConfigComposer(values), [1, 9, "group", "badge", "AA", "BB", false, 7, true]);
        Verify(new List<(int X, int Y)> { (2, 3) }, values => new RoomOccupiedTilesComposer(values), [1, 2, 3]);
        Verify(new List<KeyValuePair<string, int>> { new("tag", 5) }, values => new PopularRoomTagsResultComposer(values), [1, "tag", 5]);
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

    private static void Verify<T>(List<T> source, Func<List<T>, IServerPacket> create, object[] expected)
    {
        var composer = create(source);
        source.Clear();
        Recompose(composer, expected);
    }

    private static void Recompose(IServerPacket composer, IEnumerable<object> expected)
    {
        for (var index = 0; index < 2; index++) {
            var packet = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(packet);
            Assert.Equal(expected, packet.Writes);
        }
    }
}
