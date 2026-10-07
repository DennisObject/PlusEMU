using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Directory;

internal sealed class Game2CheckGameDirectoryStatusEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!packet.HasDataRemaining()) {
            directory.ShowDirectoryStatus(session);
        }

        return Task.CompletedTask;
    }
}
