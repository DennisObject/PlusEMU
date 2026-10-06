using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class AcceptGroupMembershipEvent(IGroupMembershipMutationService groups) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) =>
        groups.Accept(session, packet.ReadInt(), packet.ReadInt());
}
