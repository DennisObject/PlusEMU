using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Outgoing.Groups.Forums;

public sealed class ForumDataComposer(GroupForumSnapshot forum) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ForumDataComposer;

    public void Compose(IOutgoingPacket packet)
    {
        ForumWire.Forum(packet, forum);
        packet.WriteInteger(forum.Permissions.Read);
        packet.WriteInteger(forum.Permissions.Post);
        packet.WriteInteger(forum.Permissions.Start);
        packet.WriteInteger(forum.Permissions.Moderate);
        packet.WriteString(forum.ReadError);
        packet.WriteString(forum.PostError);
        packet.WriteString(forum.StartError);
        packet.WriteString(forum.ModerateError);
        packet.WriteString("");
        packet.WriteBoolean(forum.ChangeSettings);
        packet.WriteBoolean(forum.Staff);
    }
}
