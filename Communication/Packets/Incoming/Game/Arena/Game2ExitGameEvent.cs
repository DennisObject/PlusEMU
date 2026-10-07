using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Arena;

internal sealed class Game2ExitGameEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadOptionalBool(packet)) {
            manager.ExitGame(session);
        }

        return Task.CompletedTask;
    }
}
