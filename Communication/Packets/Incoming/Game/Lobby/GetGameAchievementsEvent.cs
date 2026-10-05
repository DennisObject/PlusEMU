using Plus.Communication.Packets.Outgoing.Game;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Game.Lobby;

internal class GetGameAchievementsEvent(IAchievementManager achievementManager, IAchievementSnapshotService snapshots) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var gameId = packet.ReadInt();
        session.Send(new GameAccountStatusComposer(gameId));
        session.Send(new PlayableGamesComposer(gameId));
        session.Send(new GameAchievementListComposer(gameId, snapshots.Capture(session.GetHabbo(), achievementManager.GetGameAchievements(gameId))));
        return Task.CompletedTask;
    }
}
