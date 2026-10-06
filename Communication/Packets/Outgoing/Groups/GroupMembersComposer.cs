using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class GroupMembersComposer(GroupMembersPresentation presentation) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GroupMembersComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(presentation.GroupId);
        packet.WriteString(presentation.GroupName);
        packet.WriteUInteger(presentation.RoomId);
        packet.WriteString(presentation.Badge);
        packet.WriteInteger(presentation.Total);
        packet.WriteInteger(presentation.Members.Length);

        if (presentation.Total > 0)
        {
            foreach (var member in presentation.Members)
            {
                packet.WriteInteger(member.Role);
                packet.WriteInteger(member.Id);
                packet.WriteString(member.Username);
                packet.WriteString(member.Look);
                packet.WriteString(string.Empty);
            }
        }

        packet.WriteBoolean(presentation.CanManage);
        packet.WriteInteger(14);
        packet.WriteInteger(presentation.Page);
        packet.WriteInteger(presentation.RequestType);
        packet.WriteString(presentation.Search);
    }
}
