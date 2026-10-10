using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class MakeOfferEvent(IMarketplaceListingService listings) : IPacketEvent
{
    // AIR MakeOfferMessageComposer (3676): the price the buyer pays, 1 for floor or 2 for wall furni, how many items and their ids.
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var sellingPrice = packet.ReadInt();
        var furniType = packet.ReadInt();
        var count = packet.ReadInt();

        if (count is < 1 or > MarketplaceListingService.MaximumOfferCount) {
            session.Send(new MarketplaceMakeOfferResultComposer(MarketplaceOfferResult.Rejected));

            return Task.CompletedTask;
        }

        var itemIds = new uint[count];

        for (var index = 0; index < count; index++) {
            itemIds[index] = packet.ReadUInt();
        }

        if (!listings.TryList(session.GetHabbo(), itemIds, furniType, sellingPrice)) {
            session.Send(new MarketplaceMakeOfferResultComposer(MarketplaceOfferResult.Rejected));

            return Task.CompletedTask;
        }

        foreach (var itemId in itemIds) {
            session.Send(new FurniListRemoveComposer(itemId));
        }

        session.Send(new MarketplaceMakeOfferResultComposer(MarketplaceOfferResult.Accepted));

        return Task.CompletedTask;
    }
}
