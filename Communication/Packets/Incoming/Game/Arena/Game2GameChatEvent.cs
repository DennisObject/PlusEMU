using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Arena;

internal sealed class Game2GameChatEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadString(packet, out var message)) {
            manager.Chat(session, message);
        }

        return Task.CompletedTask;
    }
}
