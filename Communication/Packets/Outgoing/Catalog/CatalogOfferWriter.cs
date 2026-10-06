using System.Globalization;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

// One catalog offer as the client parses it, shared by catalog pages and single product offers.
public static class CatalogOfferWriter
{
    public static void Write(IOutgoingPacket packet, CatalogOfferSnapshot offer)
    {
        packet.WriteInteger(offer.WireOfferId);
        packet.WriteString(offer.LocalizationId);
        packet.WriteBoolean(false); //IsRentable
        packet.WriteInteger(offer.CostCredits);
        packet.WriteInteger(offer.Price);
        packet.WriteInteger(offer.PriceType);
        packet.WriteBoolean(offer.CanGift);
        WriteProducts(packet, offer.Products);
        packet.WriteInteger(offer.ClubLevel);
        packet.WriteBoolean(offer.CanSelectAmount);
        packet.WriteBoolean(false); // TODO: Figure out
        packet.WriteString(offer.PreviewImage); // e.g. catalogue/pet_lion.png
        packet.WriteString("");
        packet.WriteBoolean(offer.OfferEnabled);
    }

    private static void WriteProducts(IOutgoingPacket packet, CatalogOfferProducts products)
    {
        switch (products)
        {
            case HabbiconProducts habbicon:
                packet.WriteInteger(1);
                packet.WriteString("habbicon");
                packet.WriteInteger(habbicon.HabbiconId);
                packet.WriteString(habbicon.HabbiconId.ToString(CultureInfo.InvariantCulture));
                packet.WriteInteger(1);
                packet.WriteBoolean(false);
                return;
            case DealProducts deal:
                packet.WriteInteger(deal.Items.Length);
                foreach (var dealItem in deal.Items)
                {
                    packet.WriteString(dealItem.ProductType);
                    if (dealItem.ProductType == "b")
                    {
                        packet.WriteString(dealItem.ItemName);
                        continue;
                    }
                    packet.WriteInteger(dealItem.SpriteId);
                    packet.WriteString("");
                    packet.WriteInteger(dealItem.Amount);
                    packet.WriteBoolean(false);
                }
                return;
            case ItemProducts item:
                WriteItem(packet, item);
                return;
        }
    }

    private static void WriteItem(IOutgoingPacket packet, ItemProducts item)
    {
        packet.WriteInteger(string.IsNullOrEmpty(item.Badge) ? 1 : 2); //Count 1 item if there is no badge, otherwise count as 2.
        if (!string.IsNullOrEmpty(item.Badge))
        {
            packet.WriteString("b");
            packet.WriteString(item.Badge);
        }
        packet.WriteString(item.ProductType);
        if (item.ProductType == "b")
        {
            //This is just a badge, append the name.
            packet.WriteString(item.ItemName);
            return;
        }
        packet.WriteInteger(item.SpriteId);
        if (item.HasExtra)
            packet.WriteString(item.Extra);
        packet.WriteInteger(item.Amount);
        packet.WriteBoolean(item.IsLimited); // IsLimited
        if (item.IsLimited)
        {
            packet.WriteUInteger(item.LimitedStack);
            packet.WriteUInteger(item.LimitedRemaining);
        }
    }
}
