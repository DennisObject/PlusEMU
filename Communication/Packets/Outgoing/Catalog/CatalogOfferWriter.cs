using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Catalog;

// One catalog offer as the client parses it, shared by catalog pages and single product offers.
public static class CatalogOfferWriter
{
    public static void Write(IOutgoingPacket packet, CatalogItem item, int offerId, string localizationId)
    {
        packet.WriteInteger(offerId);
        packet.WriteString(localizationId);
        packet.WriteBoolean(false); //IsRentable
        packet.WriteInteger(item.CostCredits);
        if (item.CostDiamonds > 0)
        {
            packet.WriteInteger(item.CostDiamonds);
            packet.WriteInteger(5); // Diamonds
        }
        else
        {
            packet.WriteInteger(item.CostPixels);
            packet.WriteInteger(0); // Type of PixelCost
        }
        packet.WriteBoolean(ItemUtility.CanGiftItem(item));
        WriteProducts(packet, item);
        packet.WriteInteger(item.ClubLevel);
        packet.WriteBoolean(ItemUtility.CanSelectAmount(item));
        packet.WriteBoolean(false); // TODO: Figure out
        packet.WriteString(item.PreviewImage ?? string.Empty); // e.g. catalogue/pet_lion.png
        packet.WriteString("");
        packet.WriteBoolean(item.HabbiconId > 0 ? item.HaveOffer : true);
    }

    private static void WriteProducts(IOutgoingPacket packet, CatalogItem item)
    {
        if (item.HabbiconId > 0)
        {
            packet.WriteInteger(1);
            packet.WriteString("habbicon");
            packet.WriteInteger(item.HabbiconId);
            packet.WriteString(item.HabbiconId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            packet.WriteInteger(1);
            packet.WriteBoolean(false);
            return;
        }
        if (item.Definition.InteractionType == InteractionType.Deal || item.Definition.InteractionType == InteractionType.Roomdeal)
        {
            if (!PlusEnvironment.Game.Catalog.TryGetDeal(item.Definition.BehaviourData, out var deal))
            {
                packet.WriteInteger(0); //Count
                return;
            }
            packet.WriteInteger(deal.ItemDataList.Count);
            foreach (var dealItem in deal.ItemDataList.ToList())
            {
                packet.WriteString(dealItem.Definition.ProductType);
                if (dealItem.Definition.ProductType == "b")
                {
                    packet.WriteString(dealItem.Definition.ItemName);
                    continue;
                }
                packet.WriteInteger(dealItem.Definition.SpriteId);
                packet.WriteString("");
                packet.WriteInteger(dealItem.Amount);
                packet.WriteBoolean(false);
            }
            return;
        }
        packet.WriteInteger(string.IsNullOrEmpty(item.Badge) ? 1 : 2); //Count 1 item if there is no badge, otherwise count as 2.
        if (!string.IsNullOrEmpty(item.Badge))
        {
            packet.WriteString("b");
            packet.WriteString(item.Badge);
        }
        packet.WriteString(item.Definition.ProductType);
        if (item.Definition.ProductType == "b")
        {
            //This is just a badge, append the name.
            packet.WriteString(item.Definition.ItemName);
            return;
        }
        packet.WriteInteger(item.Definition.SpriteId);
        if (item.Definition.InteractionType == InteractionType.Wallpaper || item.Definition.InteractionType == InteractionType.Floor || item.Definition.InteractionType == InteractionType.Landscape)
            packet.WriteString(item.CatalogName.Split('_')[2]);
        else if (item.Definition.InteractionType == InteractionType.Bot) //Bots
        {
            if (!PlusEnvironment.Game.Catalog.TryGetBot(item.ItemId, out var catalogBot))
                packet.WriteString("hd-180-7.ea-1406-62.ch-210-1321.hr-831-49.ca-1813-62.sh-295-1321.lg-285-92");
            else
                packet.WriteString(catalogBot.Figure);
        }
        else if (item.ExtraData != null) packet.WriteString(item.ExtraData);
        packet.WriteInteger(item.Amount);
        packet.WriteBoolean(item.IsLimited); // IsLimited
        if (item.IsLimited)
        {
            packet.WriteUInteger(item.LimitedEditionStack);
            packet.WriteUInteger(item.LimitedEditionStack - item.LimitedEditionSells);
        }
    }
}
