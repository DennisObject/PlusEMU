using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Catalog;

namespace Plus.Communication.Packets.Incoming.Users;

internal class GetKickbackInfoEvent(IClubCatalogService club) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
        => club.ShowKickback(session);
}
