using Plus.HabboHotel.GameClients;
using Plus.Communication.Packets.Outgoing.Game;

namespace Plus.Communication.Packets.Incoming.Game.Directory;

internal class Game2GetAccountGameStatusEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new GameAccountStatusComposer(packet.ReadInt()));
        return Task.CompletedTask;
    }
}
