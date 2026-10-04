using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogPageComposer : IServerPacket
{
    private readonly CatalogPageSnapshot _page;
    public uint MessageId => ServerPacketHeader.CatalogPageComposer;

    public CatalogPageComposer(CatalogPageSnapshot page)
    {
        _page = page;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_page.Id);
        packet.WriteString(_page.Mode);
        packet.WriteString(_page.Layout);
        packet.WriteInteger(_page.Strings1.Count);
        foreach (var s in _page.Strings1) packet.WriteString(s);
        packet.WriteInteger(_page.Strings2.Count);
        foreach (var s in _page.Strings2) packet.WriteString(s);
        packet.WriteInteger(_page.Offers.Count);
        foreach (var offer in _page.Offers)
            CatalogOfferWriter.Write(packet, offer);
        packet.WriteInteger(_page.PreselectOfferId);
        packet.WriteBoolean(false);
        packet.WriteInteger(_page.Promotions.Count);
        foreach (var promotion in _page.Promotions)
        {
            packet.WriteInteger(promotion.Position);
            packet.WriteString(promotion.Title);
            packet.WriteString(promotion.Image);
            packet.WriteInteger(promotion.ItemType);
            switch (promotion.ItemType)
            {
                case CatalogPromotion.ProductOfferItem:
                    packet.WriteInteger(promotion.OfferId);
                    break;
                case CatalogPromotion.ProductCodeItem:
                    packet.WriteString(promotion.ProductCode ?? string.Empty);
                    break;
                default:
                    packet.WriteString(promotion.PageLink);
                    break;
            }
            packet.WriteInteger(promotion.SecondsLeft);
        }
    }
}
