using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Incoming.Groups.Forums
{
    internal sealed class UpdateThreadEvent(IGroupForumService service) : IPacketEvent
    {
        public Task Parse(GameClient session, IIncomingPacket packet)
        {
            service.UpdateThread(session, packet.ReadInt(), packet.ReadInt(), packet.ReadBool(), packet.ReadBool());

            return Task.CompletedTask;
        }
    }
}
