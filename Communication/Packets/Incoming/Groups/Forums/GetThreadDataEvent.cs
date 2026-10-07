using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups.Forums;

namespace Plus.Communication.Packets.Incoming.Groups.Forums;

internal sealed class GetThreadDataEvent(IGroupForumService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        service.ShowMessages(session, packet.ReadInt(), packet.ReadInt(), packet.ReadInt(), packet.ReadInt());

        return Task.CompletedTask;
    }
}
