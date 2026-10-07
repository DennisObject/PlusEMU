using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Incoming.Groups.Forums
{
    internal sealed class GetForumsUnreadCountEvent(IGroupForumService service) : IPacketEvent
    {
        public Task Parse(GameClient session, IIncomingPacket packet)
        {
            service.ShowUnread(session);

            return Task.CompletedTask;
        }
    }
}
