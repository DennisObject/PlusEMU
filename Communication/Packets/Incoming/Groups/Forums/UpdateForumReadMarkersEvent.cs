using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Incoming.Groups.Forums
{
    internal sealed class UpdateForumReadMarkersEvent(IGroupForumService service) : IPacketEvent
    {
        public Task Parse(GameClient session, IIncomingPacket packet)
        {
            var count = packet.ReadInt();
            if (count is < 0 or > 100) {
                return Task.CompletedTask;
            }
            var markers = new ForumReadMarker[count];
            for (var i = 0; i < count; i++) {
                markers[i] = new ForumReadMarker(packet.ReadInt(), packet.ReadInt(), packet.ReadBool());
            }
            service.MarkRead(session, markers);

            return Task.CompletedTask;
        }
    }
}
