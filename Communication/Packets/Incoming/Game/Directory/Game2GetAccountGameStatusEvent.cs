using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Directory;

internal sealed class Game2GetAccountGameStatusEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 1, out var values)) {
            directory.ShowAccountStatus(session, values[0]);
        }

        return Task.CompletedTask;
    }
}
