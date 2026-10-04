using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class ClubGiftsComposer(ClubGiftInfo info) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ClubGiftsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(info.DaysUntilNextGift);
        packet.WriteInteger(info.Available);
        packet.WriteInteger(info.Gifts.Count);
        foreach (var gift in info.Gifts)
        {
            var item = gift.Item;
            // Gifts are free, regardless of the ordinary catalog price of the same chair.
            CatalogOfferWriter.Write(packet, new Plus.HabboHotel.Catalog.CatalogItem {
                Definition = item.Definition, Amount = item.Amount, CatalogName = item.CatalogName,
                ExtraData = "", ClubLevel = item.ClubLevel, PreviewImage = item.PreviewImage
            }, item.WireOfferId, item.CatalogName);
        }
        packet.WriteInteger(info.Gifts.Count);
        foreach (var gift in info.Gifts)
        {
            packet.WriteInteger(gift.Item.WireOfferId);
            packet.WriteBoolean(false); // one HC membership
            packet.WriteInteger(gift.DaysRequired);
            packet.WriteBoolean(info.Available > 0 && info.PastDays >= gift.DaysRequired);
        }
    }
}