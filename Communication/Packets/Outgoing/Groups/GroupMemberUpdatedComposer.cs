using Plus.Communication.Packets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class GroupMemberUpdatedComposer : IServerPacket
{
    private readonly GroupMemberUpdateSnapshot _snapshot;
    public uint MessageId => ServerPacketHeader.GroupMemberUpdatedComposer;

    public GroupMemberUpdatedComposer(GroupMemberUpdateSnapshot snapshot) => _snapshot = snapshot;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_snapshot.GroupId);
        packet.WriteInteger(_snapshot.Role);
        packet.WriteInteger(_snapshot.UserId);
        packet.WriteString(_snapshot.Username);
        packet.WriteString(_snapshot.Look);
        packet.WriteString(string.Empty);
    }
}
