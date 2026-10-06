using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public sealed class PurchaseFromCatalogEvent(ICatalogPurchaseService purchases) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var request = new CatalogPurchaseRequest(
            packet.ReadInt(),
            packet.ReadInt(),
            packet.ReadString(),
            packet.ReadInt());

        return purchases.Purchase(session, request);
    }
}
