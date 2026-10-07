using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Outgoing.Groups.Forums;

public sealed class PostUpdatedComposer(int groupId, int threadId, ForumMessageSnapshot message) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PostUpdatedComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(groupId);
        packet.WriteInteger(threadId);
        ForumWire.Message(packet, message);
    }
}
