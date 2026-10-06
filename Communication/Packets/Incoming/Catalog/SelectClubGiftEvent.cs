using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class SelectClubGiftEvent(IClubCatalogService clubCatalog) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        clubCatalog.ClaimGift(session, packet.ReadString());
}
