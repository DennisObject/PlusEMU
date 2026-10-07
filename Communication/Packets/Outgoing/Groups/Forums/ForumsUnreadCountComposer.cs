using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Outgoing.Groups.Forums
{
    public sealed class ForumsUnreadCountComposer(int count) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.ForumsUnreadCountComposer;

        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteInteger(count);
        }
    }
}
