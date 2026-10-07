using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Ingame;

/// <summary>AIR reason: 0 = too far behind, 1 = checksum mismatch, -1 = forced.</summary>
internal sealed class Game2RequestFullStatusUpdateEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 1, out var values)) {
            manager.RequestFullStatus(session, values[0]);
        }

        return Task.CompletedTask;
    }
}
