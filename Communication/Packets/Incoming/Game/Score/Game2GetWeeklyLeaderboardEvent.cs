using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;

namespace Plus.Communication.Packets.Incoming.Game.Score;

internal class Game2GetWeeklyLeaderboardEvent : IPacketEvent
{
    private readonly IGameDataManager _gameDataManager;

    public Game2GetWeeklyLeaderboardEvent(IGameDataManager gameDataManager)
    {
        _gameDataManager = gameDataManager;
    }


    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var gameId = packet.ReadInt();

        if (_gameDataManager.TryGetGame(gameId, out var gameData)) {
            //Code
        }

        return Task.CompletedTask;
    }
}
