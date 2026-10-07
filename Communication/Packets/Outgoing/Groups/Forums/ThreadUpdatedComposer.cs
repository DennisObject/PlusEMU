using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Outgoing.Groups.Forums
{
    public sealed class ThreadUpdatedComposer(int groupId, ForumThreadSnapshot thread) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.ThreadUpdatedComposer;

        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteInteger(groupId);
            ForumWire.Thread(packet, thread);
        }
    }
}
