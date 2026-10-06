using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public sealed class PurchaseFromCatalogAsGiftEvent(ICatalogGiftPurchaseService purchases) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var request = new CatalogGiftPurchaseRequest(
            packet.ReadInt(),
            packet.ReadInt(),
            packet.ReadString(),
            packet.ReadString(),
            packet.ReadString(),
            packet.ReadInt(),
            packet.ReadInt(),
            packet.ReadInt(),
            packet.ReadBool());

        return purchases.Purchase(session, request);
    }
}
