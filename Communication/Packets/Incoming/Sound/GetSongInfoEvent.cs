using Plus.Communication.Packets.Outgoing.Sound;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Sound;

internal class GetSongInfoEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new TraxSongInfoComposer());

        return Task.CompletedTask;
    }
}
