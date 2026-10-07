using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Arena;

/// <summary>AIR sends LoadStageReady(100) once the arena room objects are initialised.</summary>
internal sealed class Game2LoadStageReadyEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 1, out var values) && values[0] is >= 0 and <= 100) {
            manager.LoadStageReady(session);
        }

        return Task.CompletedTask;
    }
}
