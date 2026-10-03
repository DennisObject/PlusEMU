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

    public void Compose(IOutgoingPacket packet) =>
        CatalogOfferWriter.Write(packet, _item, _item.WireOfferId, _item.HabbiconId > 0 ? _item.CatalogName : _item.Definition.ItemName);
}
