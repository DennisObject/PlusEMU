using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Marketplace;

public class MarketplaceCancelOfferResultComposer : IServerPacket
{
    private readonly uint _offerId;
    private readonly bool _success;
    public uint MessageId => ServerPacketHeader.MarketplaceCancelOfferResultComposer;

    public MarketplaceCancelOfferResultComposer(uint offerId, bool success)
    {
        _offerId = offerId;
        _success = success;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_offerId);
        packet.WriteBoolean(_success);
    }
}
