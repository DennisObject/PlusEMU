using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Marketplace;

// The values the AIR client's buy-result handler switches on.
public enum MarketplaceBuyResult
{
    RefreshOffers = 1,
    OfferGone = 2,
    OfferReplaced = 3,
    NotEnoughCredits = 4,
}

public class MarketplaceBuyOfferResultComposer : IServerPacket
{
    private readonly MarketplaceBuyResult _result;
    private readonly int _newOfferId;
    private readonly int _newPrice;
    private readonly int _requestedOfferId;
    public uint MessageId => ServerPacketHeader.MarketplaceBuyOfferResultComposer;

    // The new offer id and price only matter to OfferReplaced; every other result leaves them at zero.
    public MarketplaceBuyOfferResultComposer(MarketplaceBuyResult result, int requestedOfferId, int newOfferId = 0, int newPrice = 0)
    {
        _result = result;
        _requestedOfferId = requestedOfferId;
        _newOfferId = newOfferId;
        _newPrice = newPrice;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger((int)_result);
        packet.WriteInteger(_newOfferId);
        packet.WriteInteger(_newPrice);
        packet.WriteInteger(_requestedOfferId);
    }
}
