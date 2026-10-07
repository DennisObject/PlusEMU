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

/// <summary>Arena voting (Plus extra): the field type of the arena the player wants.</summary>
internal sealed class Game2VoteArenaEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 1, out var values)) {
            manager.VoteArena(session, values[0]);
        }

        return Task.CompletedTask;
    }
}
