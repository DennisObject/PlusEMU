using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Lobby;

/// <summary>AIR "Play": join (or create) a SnowStorm lobby.</summary>
internal sealed class Game2QuickJoinEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!packet.HasDataRemaining()) {
            manager.QuickJoin(session);
        }

        return Task.CompletedTask;
    }
}

internal sealed class Game2LeaveLobbyEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!packet.HasDataRemaining()) {
            manager.LeaveLobby(session);
        }

        return Task.CompletedTask;
    }
}
