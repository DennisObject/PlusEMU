using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Marketplace;

public class MarketplaceMakeOfferResultComposer : IServerPacket
{
    private readonly MarketplaceOfferResult _success;
    public uint MessageId => ServerPacketHeader.MarketplaceMakeOfferResultComposer;

    public MarketplaceMakeOfferResultComposer(MarketplaceOfferResult success)
    {
        _success = success;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger((int)_success);
}