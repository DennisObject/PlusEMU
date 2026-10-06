using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class DeclineGroupMembershipEvent(IGroupMembershipMutationService groups) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        groups.Decline(session, packet.ReadInt(), packet.ReadInt());
}
