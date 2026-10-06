using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Tracking;

internal class EventTrackerEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => Task.CompletedTask;
}
