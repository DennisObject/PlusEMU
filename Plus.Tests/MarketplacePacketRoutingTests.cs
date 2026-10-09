using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Marketplace;
using Xunit;

namespace Plus.Tests;

// The marketplace requests the AIR client (WIN63-202609161723-93809945) sends, with the id of the composer each HabboCatalog method uses (communication/class_2036.as composer table):
// getOwnMarketPlaceOffers class_1889 3217, buyMarketPlaceOffer class_1883 3948, redeemSoldMarketPlaceOffers class_1774 1229, redeemExpiredMarketPlaceOffer class_1825 802.
public class MarketplacePacketRoutingTests
{
    private static readonly (Type Handler, uint Id)[] Expected =
    [
        (typeof(GetMarketplaceItemStatsEvent), 994),
        (typeof(MakeOfferEvent), 3676),
        (typeof(GetMarketplaceCanMakeOfferEvent), 2865),
        (typeof(GetOffersEvent), 324),
        (typeof(GetOwnOffersEvent), 3217),
        (typeof(BuyOfferEvent), 3948),
        (typeof(RedeemOfferCreditsEvent), 1229),
        (typeof(CancelOfferEvent), 802)
    ];

    [Fact]
    public void EveryMarketplaceRequestIsDispatchedToItsOwnHandlerUnderTheAirId()
    {
        var handlers = Expected.Select(entry => (IPacketEvent)RuntimeHelpers.GetUninitializedObject(entry.Handler)).ToArray();
        var manager = new PacketManager(handlers, NullLogger<PacketManager>.Instance);
        var registered = (Dictionary<uint, IPacketEvent>)typeof(PacketManager).GetField("_incomingPackets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;

        foreach (var (handler, id) in Expected) {
            Assert.True(manager.IsRegistered(id), $"{handler.Name} is not registered under {id}");
            Assert.Equal(handler, registered[id].GetType());
        }

        Assert.Equal(Expected.Length, registered.Count);
    }

    [Fact]
    public void TheDefaultRevisionFileCarriesTheSameMarketplaceIds()
    {
        using var revision = JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Resources/Revisions/example.json")));
        var incoming = revision.RootElement.GetProperty("IncomingHeaders");

        foreach (var (handler, id) in Expected) {
            Assert.Equal(id, incoming.GetProperty(handler.Name).GetUInt32());
        }
    }
}
