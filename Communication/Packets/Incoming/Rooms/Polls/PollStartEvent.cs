using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Polls;

namespace Plus.Communication.Packets.Incoming.Rooms.Polls;

internal sealed class PollStartEvent(IRoomPollService polls) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        polls.Start(session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
