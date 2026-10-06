using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Navigator.New;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class NavigatorSearchSnapshotTests
{
    [Fact]
    public void ComposerWritesExactCategoryAndRoomBlockFields()
    {
        var room = new RoomWireData(42, "HQ", 7, "Alice", 1, 3, 25, "Desc", 2, 9, 4, ["one", "two"], true, true, "featured.png",
            new(8, "Group", "badge"), new("Promo", "Details", 12));
        var snapshot = new NavigatorSearchSnapshot("hotel_view", "query", [new("popular", "Popular", 1, 0, [room])]);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new NavigatorSearchResultSetComposer(snapshot).Compose(packet);

        Assert.Equal(new object[] { "hotel_view", "query", 1, "popular", "Popular", 1, false, 0, 1, (uint)42, "HQ", 7, "Alice", 1, 3, 25,
            "Desc", 2, 9, 0, 4, 2, "one", "two", 31, "featured.png", 8, "Group", "badge", "Promo", "Details", 12 }, packet.Writes);
    }

    [Fact]
    public void ComposerReusesFrozenSnapshotWithoutSessionRoomOrQueryAccess()
    {
        var mutableTags = new[] { "original" };
        var room = new RoomWireData(1, "Room", 2, "Owner", 0, 1, 10, "", 0, 0, 0, mutableTags.ToImmutableArray(), false, false, null, null, null);
        var composer = new NavigatorSearchResultSetComposer(new("cat", "", [new("id", "name", 0, 1, [room])]));
        var first = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(first);

        mutableTags[0] = "changed";
        var second = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(second);

        Assert.Equal(first.Writes, second.Writes);
        Assert.Contains("original", second.Writes);
        Assert.DoesNotContain("changed", second.Writes);
        var field = Assert.Single(typeof(NavigatorSearchResultSetComposer).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
        Assert.Equal(typeof(NavigatorSearchSnapshot), field.FieldType);
    }
}
