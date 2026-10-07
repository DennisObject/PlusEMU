using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Arena;

internal sealed class Game2PlayAgainEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!packet.HasDataRemaining()) {
            manager.PlayAgain(session);
        }

        return Task.CompletedTask;
    }
}
