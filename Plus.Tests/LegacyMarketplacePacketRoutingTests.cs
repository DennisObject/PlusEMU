using System.Text.Json;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Revisions;
using Xunit;

namespace Plus.Tests;

public class LegacyMarketplacePacketRoutingTests
{
    [Theory]
    [InlineData("1.6.6.json")]
    [InlineData("3.6.0.json")]
    [InlineData("OCTANE-3-6-0-FLOOR-20260909.json")]
    public void LegacyMarketplaceRequestsTranslateToTheirOwnOperations(string filename)
    {
        var revision = Assert.IsType<Revision>(JsonSerializer.Deserialize<Revision>(
            File.ReadAllText(HabbiconPacketTests.Repo($"Resources/Revisions/{filename}"))));
        revision.BuildMappings(new RevisionsCache().InternalRevision);

        // NitroMessages registers these legacy headers to own-list, buy, redeem and cancel composers.
        Assert.Equal(ClientPacketHeader.GetOwnOffersEvent, revision.IncomingIdToInternalIdMapping[2105]);
        Assert.Equal(ClientPacketHeader.BuyOfferEvent, revision.IncomingIdToInternalIdMapping[1603]);
        Assert.Equal(ClientPacketHeader.RedeemOfferCreditsEvent, revision.IncomingIdToInternalIdMapping[2650]);
        Assert.Equal(ClientPacketHeader.CancelOfferEvent, revision.IncomingIdToInternalIdMapping[434]);
        Assert.False(revision.IncomingIdToInternalIdMapping.ContainsKey(2283));
    }
}
