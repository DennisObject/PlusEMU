using System.Text.Json;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Xunit;

namespace Plus.Tests;

public class WiredRewardPacketRegistrationTests
{
    [Fact]
    public void ConcreteRewardResultUsesActiveCollisionFreeProfileMapping()
    {
        var revision = new Plus.Communication.Revisions.RevisionsCache().InternalRevision;
        var composer = new WiredRewardResultComposer(5);
        Assert.Equal(ServerPacketHeader.WiredRewardResultComposer, composer.MessageId);
        var wire = revision.OutgoingHeaders[nameof(ServerPacketHeader.WiredRewardResultComposer)];
        Assert.Equal(ServerPacketHeader.WiredRewardResultComposer, wire);
        Assert.Equal(wire, revision.InternalIdToOutgoingIdMapping[composer.MessageId]);
        Assert.Single(revision.OutgoingHeaders, pair => pair.Value == wire);
    }
}
