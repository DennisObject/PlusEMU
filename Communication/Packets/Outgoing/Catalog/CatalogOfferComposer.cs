using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogOfferComposer : IServerPacket
{
    private readonly CatalogItem _item;
    public uint MessageId => ServerPacketHeader.CatalogOfferComposer;

    public CatalogOfferComposer(CatalogItem item)
    {
        _item = item;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_item.OfferId);
        packet.WriteString(_item.HabbiconId > 0 ? _item.CatalogName : _item.Definition.ItemName);
        packet.WriteBoolean(false); //IsRentable
        packet.WriteInteger(_item.CostCredits);
        if (_item.CostDiamonds > 0)
        {
            packet.WriteInteger(_item.CostDiamonds);
            packet.WriteInteger(5); // Diamonds
        }
        else
        {
            packet.WriteInteger(_item.CostPixels);
            packet.WriteInteger(0); // Type of PixelCost
        }
        packet.WriteBoolean(ItemUtility.CanGiftItem(_item));
        if (_item.HabbiconId > 0)
        {
            packet.WriteInteger(1);
            packet.WriteString("habbicon");
            packet.WriteInteger(_item.HabbiconId);
            packet.WriteString(_item.HabbiconId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            packet.WriteInteger(1);
            packet.WriteBoolean(false);
        }
        else
        {
            packet.WriteInteger(string.IsNullOrEmpty(_item.Badge) ? 1 : 2); //Count 1 item if there is no badge, otherwise count as 2.
            if (!string.IsNullOrEmpty(_item.Badge))
            {
                packet.WriteString("b");
                packet.WriteString(_item.Badge);
            }
            packet.WriteString(_item.Definition.ProductType);
            if (_item.Definition.ProductType == "b")
                packet.WriteString(_item.Definition.ItemName); //Badge name.
            else
            {
                packet.WriteInteger(_item.Definition.SpriteId);
                if (_item.Definition.InteractionType == InteractionType.Wallpaper || _item.Definition.InteractionType == InteractionType.Floor || _item.Definition.InteractionType == InteractionType.Landscape)
                    packet.WriteString(_item.CatalogName.Split('_')[2]);

                // TODO @80O: Dont make this static hardcoded page 9
                else if (_item.PageId == 9) //Bots
                {
                    CatalogBot cataBot = null;
                    if (!PlusEnvironment.Game.Catalog.TryGetBot(_item.ItemId, out cataBot))
                        packet.WriteString("hd-180-7.ea-1406-62.ch-210-1321.hr-831-49.ca-1813-62.sh-295-1321.lg-285-92");
                    else
                        packet.WriteString(cataBot.Figure);
                }
                else if (_item.ExtraData != null)
                    packet.WriteString(_item.ExtraData != null ? _item.ExtraData : string.Empty);
                packet.WriteInteger(_item.Amount);
                packet.WriteBoolean(_item.IsLimited); // IsLimited
                if (_item.IsLimited)
                {
                    packet.WriteUInteger(_item.LimitedEditionStack);
                    packet.WriteUInteger(_item.LimitedEditionStack - _item.LimitedEditionSells);
                }
            }
        }
        packet.WriteInteger(0); // club_level
        packet.WriteBoolean(ItemUtility.CanSelectAmount(_item));
        packet.WriteBoolean(false); // TODO: Figure out
        packet.WriteString(""); //previewImage -> e.g; catalogue/pet_lion.png
        packet.WriteString("");
        packet.WriteBoolean(_item.HabbiconId > 0 ? _item.HaveOffer : true);
    }
}
