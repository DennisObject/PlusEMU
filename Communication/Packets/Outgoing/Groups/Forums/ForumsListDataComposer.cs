using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Outgoing.Groups.Forums
{
    public sealed class ForumsListDataComposer(GroupForumsPage page) : IServerPacket
    {
        public uint MessageId => ServerPacketHeader.ForumsListDataComposer;

        public void Compose(IOutgoingPacket packet)
        {
            packet.WriteInteger(page.Kind);
            packet.WriteInteger(page.Total);
            packet.WriteInteger(page.Start);
            packet.WriteInteger(page.Forums.Length);

            foreach (var forum in page.Forums) {
                ForumWire.Forum(packet, forum);
            }
        }
    }
}
