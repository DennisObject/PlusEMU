using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class GetGroupMembersEvent(IGroupPresentationService presentation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        presentation.ShowMembers(session, new GroupMembersRequest(
            packet.ReadInt(),
            packet.ReadInt(),
            packet.ReadString(),
            packet.ReadInt()));

        return Task.CompletedTask;
    }
}
