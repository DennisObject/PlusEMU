using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal abstract class PurchaseMembershipExtensionEvent(IClubCatalogService clubCatalog) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        clubCatalog.PurchaseMembership(session, packet.ReadInt());
}
