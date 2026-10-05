using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class GroupManagementSnapshotTests
{
    [Theory]
    [InlineData("b0101s02023", 1, 1, 0, 2, 2, 3)]
    [InlineData("b123451", 123, 45, 1, 0, 0, 0)]
    public void BadgeParserProducesFiveBoundedTriplets(string badge, int symbol1, int colour1, int position1, int symbol2, int colour2, int position2)
    {
        Assert.True(GroupManagementSnapshotService.TryParseBadge(badge, out var pieces));
        Assert.Equal(5, pieces.Length);
        Assert.Equal(new GroupBadgePiece(symbol1, colour1, position1), pieces[0]);
        Assert.Equal(new GroupBadgePiece(symbol2, colour2, position2), pieces[1]);
    }

    [Theory]
    [InlineData("b1")]
    [InlineData("b010101s020202s030303s040404s050505s060606")]
    [InlineData("b01xx")]
    public void MalformedBadgeIsRejected(string badge) =>
        Assert.False(GroupManagementSnapshotService.TryParseBadge(badge, out _));

    [Fact]
    public void ComposerRecomposesFromImmutableNoRoomSnapshot()
    {
        var pieces = ImmutableArray.Create(new GroupBadgePiece(1, 2, 3), default, default, default, default);
        var snapshot = new GroupManagementSnapshot(false, 0, string.Empty, 7, "group", "description", 4, 5, 2, 1, pieces, "b01023", 6, true);
        var composer = new ManageGroupComposer(snapshot);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());

        client.Send(composer);
        client.Send(composer);

        Assert.Equal(2, sent.Count);
        Assert.Equal(sent[0].Payload, sent[1].Payload);
    }

    [Fact]
    public void RoomSnapshotRetainsMissingRoomNameAndLockedType()
    {
        var snapshot = new GroupManagementSnapshot(true, 9, string.Empty, 7, "group", "description", 4, 5, 1, 0,
            ImmutableArray.Create(default(GroupBadgePiece), default, default, default, default), "", 0, false);
        Assert.True(snapshot.HasRoom);
        Assert.Equal(string.Empty, snapshot.RoomName);
        Assert.Equal(1, snapshot.Type);
    }
}
