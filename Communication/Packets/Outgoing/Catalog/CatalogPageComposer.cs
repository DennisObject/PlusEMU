using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogPageComposer : IServerPacket
{
    private readonly CatalogPage _page;
    private readonly string _mode;
    private readonly int _offerId;
    public uint MessageId => ServerPacketHeader.CatalogPageComposer;

    // offerId is the wire offer id the client asked to preselect, or -1.
    public CatalogPageComposer(CatalogPage page, string mode, int offerId = -1)
    {
        _page = page;
        _mode = mode;
        _offerId = offerId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_page.Id);
        packet.WriteString(_mode);
        packet.WriteString(_page.Layout);
        packet.WriteInteger(_page.PageStringsList1.Count);
        foreach (var s in _page.PageStringsList1) packet.WriteString(s);
        packet.WriteInteger(_page.PageStringsList2.Count);
        foreach (var s in _page.PageStringsList2) packet.WriteString(s);
        if (!_page.Layout.Equals("frontpage") && !_page.Layout.Equals("club_buy"))
        {
            packet.WriteInteger(_page.Offers.Count);
            foreach (var item in _page.Offers.Values)
                CatalogOfferWriter.Write(packet, item, item.WireOfferId, item.CatalogName);
        }
        else
            packet.WriteInteger(0);
        packet.WriteInteger(_offerId);
        packet.WriteBoolean(false);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var promotions = PlusEnvironment.Game.Catalog.Promotions.Where(promotion => !promotion.HasExpired(now)).OrderBy(promotion => promotion.Position).ToList();
        packet.WriteInteger(promotions.Count);
        foreach (var promotion in promotions)
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
            packet.WriteInteger(promotion.SecondsLeft(now));
        }
    }
}
