using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogOfferComposer : IServerPacket
{
    private readonly CatalogOfferSnapshot _offer;
    public uint MessageId => ServerPacketHeader.CatalogOfferComposer;

    public CatalogOfferComposer(CatalogOfferSnapshot offer)
    {
        _offer = offer;
    }

    public void Compose(IOutgoingPacket packet) => CatalogOfferWriter.Write(packet, _offer);
}
