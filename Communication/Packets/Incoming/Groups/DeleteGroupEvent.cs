using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Incoming.Groups;

internal class DeleteGroupEvent(IGroupRemovalService groups) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var groupId = packet.ReadInt();
        return groups.Delete(session, groupId);
    }
}
