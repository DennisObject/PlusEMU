using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Ingame;

/// <summary>Move target in world units (tile * 3200), stamped with the client's turn and subturn.</summary>
internal sealed class Game2SetUserMoveTargetEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 4, out var values)) {
            manager.SetMoveTarget(session, values[0], values[1], values[2], values[3]);
        }

        return Task.CompletedTask;
    }
}

internal sealed class Game2ThrowSnowballAtPositionEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 5, out var values)) {
            manager.ThrowAtPosition(session, values[0], values[1], values[2], values[3], values[4]);
        }

        return Task.CompletedTask;
    }
}

internal sealed class Game2ThrowSnowballAtHumanEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 4, out var values)) {
            manager.ThrowAtHuman(session, values[0], values[1], values[2], values[3]);
        }

        return Task.CompletedTask;
    }
}

internal sealed class Game2MakeSnowballEvent(ISnowStormManager manager) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 2, out var values)) {
            manager.MakeSnowball(session, values[0], values[1]);
        }

        return Task.CompletedTask;
    }
}
