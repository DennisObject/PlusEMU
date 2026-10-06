using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class MakeOfferEvent(IMarketplaceListingService listings) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var sellingPrice = packet.ReadInt();
        packet.ReadInt(); //comission
        var itemId = packet.ReadUInt();

        if (!listings.TryList(session.GetHabbo(), itemId, sellingPrice)) {
            session.Send(new MarketplaceMakeOfferResultComposer(MarketplaceOfferResult.Rejected));

            return Task.CompletedTask;
        }

        session.Send(new FurniListRemoveComposer(itemId));
        session.Send(new MarketplaceMakeOfferResultComposer(MarketplaceOfferResult.Accepted));

        return Task.CompletedTask;
    }
}
