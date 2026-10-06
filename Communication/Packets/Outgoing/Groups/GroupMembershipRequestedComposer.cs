using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class GroupMembershipRequestedComposer : IServerPacket
{
    private readonly GroupMemberUpdateSnapshot _snapshot;
    public uint MessageId => ServerPacketHeader.GroupMembershipRequestedComposer;

    public GroupMembershipRequestedComposer(GroupMemberUpdateSnapshot snapshot) => _snapshot = snapshot;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_snapshot.GroupId); //GroupId
        packet.WriteInteger(_snapshot.Role); //Type?
        {
            packet.WriteInteger(_snapshot.UserId); //UserId
            packet.WriteString(_snapshot.Username);
            packet.WriteString(_snapshot.Look);
            packet.WriteString(string.Empty);
        }
    }
}
