using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Score;

internal static class SnowStormLeaderboardReader
{
    // Total tables: gameTypeId, startRank, direction, viewSize, windowSize.
    public static Task Total(ISnowStormDirectory directory, GameClient session, IIncomingPacket packet, SnowStormLeaderboardKind kind)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 5, out var values)) {
            directory.ShowLeaderboard(session, kind, values[0], 0, values[1], values[3], values[4]);
        }

        return Task.CompletedTask;
    }

    // Weekly tables add the week offset after the game type.
    public static Task Weekly(ISnowStormDirectory directory, GameClient session, IIncomingPacket packet, SnowStormLeaderboardKind kind)
    {
        if (SnowStormPacketReader.TryReadInts(packet, 6, out var values)) {
            directory.ShowLeaderboard(session, kind, values[0], values[1], values[2], values[4], values[5]);
        }

        return Task.CompletedTask;
    }
}

internal sealed class Game2GetTotalLeaderboardEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => SnowStormLeaderboardReader.Total(directory, session, packet, SnowStormLeaderboardKind.Total);
}

internal sealed class Game2GetFriendsLeaderboardEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => SnowStormLeaderboardReader.Total(directory, session, packet, SnowStormLeaderboardKind.Friends);
}

internal sealed class Game2GetWeeklyFriendsLeaderboardEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => SnowStormLeaderboardReader.Weekly(directory, session, packet, SnowStormLeaderboardKind.WeeklyFriends);
}

internal sealed class Game2GetTotalGroupLeaderboardEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => SnowStormLeaderboardReader.Total(directory, session, packet, SnowStormLeaderboardKind.TotalGroup);
}

internal sealed class Game2GetWeeklyGroupLeaderboardEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => SnowStormLeaderboardReader.Weekly(directory, session, packet, SnowStormLeaderboardKind.WeeklyGroup);
}
