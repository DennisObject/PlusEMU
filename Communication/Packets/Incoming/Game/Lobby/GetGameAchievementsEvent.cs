using Plus.Communication.Packets.Outgoing.Game;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Game.Lobby;

internal class GetGameAchievementsEvent : IPacketEvent
{
    private readonly IAchievementManager _achievementManager;

    public GetGameAchievementsEvent(IAchievementManager achievementManager)
    {
        _achievementManager = achievementManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var gameId = packet.ReadInt();
        session.Send(new GameAccountStatusComposer(gameId));
        session.Send(new PlayableGamesComposer(gameId));
        session.Send(new GameAchievementListComposer(session, _achievementManager.GetGameAchievements(gameId), gameId));
        return Task.CompletedTask;
    }
}