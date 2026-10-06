using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

public sealed class TriggerHabbiconEvent(IRoomHabbiconService habbicons) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var id = packet.ReadInt();
        habbicons.Trigger(session, id);

        return Task.CompletedTask;
    }
}
