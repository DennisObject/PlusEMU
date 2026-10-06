using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class GetClubGiftInfoEvent(IClubCatalogService clubCatalog) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => clubCatalog.ShowGifts(session);
}
