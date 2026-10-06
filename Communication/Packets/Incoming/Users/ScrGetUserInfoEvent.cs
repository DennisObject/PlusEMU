using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Users;

internal class ScrGetUserInfoEvent(IClubCatalogService club) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
        => club.ShowStatus(session, packet.ReadString());
}
