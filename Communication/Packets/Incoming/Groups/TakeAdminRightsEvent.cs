using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class TakeAdminRightsEvent(IGroupMembershipMutationService groups) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        groups.TakeAdmin(session, packet.ReadInt(), packet.ReadInt());
}
