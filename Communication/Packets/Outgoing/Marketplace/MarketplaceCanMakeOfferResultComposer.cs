using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Marketplace;

public class MarketplaceCanMakeOfferResultComposer : IServerPacket
{
    private readonly MarketplaceOfferEligibility _result;
    public uint MessageId => ServerPacketHeader.MarketplaceCanMakeOfferResultComposer;

    public MarketplaceCanMakeOfferResultComposer(MarketplaceOfferEligibility result)
    {
        _result = result;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger((int)_result);
        packet.WriteInteger(0);
    }
}