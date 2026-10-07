using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Incoming.Groups.Forums;

internal sealed class PostGroupContentEvent(IGroupForumService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        service.Post(session, packet.ReadInt(), packet.ReadInt(), packet.ReadString(), packet.ReadString());

        return Task.CompletedTask;
    }
}
