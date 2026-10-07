using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;

namespace Plus.Communication.Packets.Incoming.Game.Score;

/// <summary>gameTypeId, weekOffset, startRank (-1 = around me), direction, viewSize, windowSize.</summary>
internal sealed class Game2GetWeeklyLeaderboardEvent(ISnowStormDirectory directory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => SnowStormLeaderboardRequest.Weekly(directory, session, packet, SnowStormLeaderboardKind.Weekly);
}
