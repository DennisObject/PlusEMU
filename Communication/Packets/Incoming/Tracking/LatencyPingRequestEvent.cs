using Plus.Communication.Packets.Outgoing.Misc;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Tracking;

internal class LatencyPingRequestEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new LatencyPingResponseComposer(packet.ReadInt()));
        return Task.CompletedTask;
    }
}
