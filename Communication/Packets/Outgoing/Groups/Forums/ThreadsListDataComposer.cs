using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Outgoing.Groups.Forums
{
    public sealed class ThreadsListDataComposer(ForumThreadsPage page) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.ThreadsListDataComposer;

        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteInteger(page.GroupId);
            packet.WriteInteger(page.Start);
            packet.WriteInteger(page.Threads.Length);

            foreach (var thread in page.Threads) {
                ForumWire.Thread(packet, thread);
            }
        }
    }
}
