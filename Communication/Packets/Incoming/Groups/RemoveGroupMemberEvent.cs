using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class RemoveGroupMemberEvent(IGroupRemovalService groups) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        var userId = packet.ReadInt();
        if (packet.HasDataRemaining())
            packet.ReadBool();
        return groups.Remove(session, groupId, userId);
    }
}
