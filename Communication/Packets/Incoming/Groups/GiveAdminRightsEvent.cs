using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class GiveAdminRightsEvent(IGroupMembershipMutationService groups) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        groups.GiveAdmin(session, packet.ReadInt(), packet.ReadInt());
}
