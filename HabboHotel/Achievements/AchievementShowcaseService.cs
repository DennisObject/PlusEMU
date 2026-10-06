using Plus.Communication.Packets.Outgoing.Game;
using Plus.Communication.Packets.Outgoing.Inventory.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Achievements;

public interface IAchievementShowcaseService
{
    void ShowDefinitions(GameClient session);
    void ShowAchievements(GameClient session);
    void ShowGameAchievements(GameClient session, int gameId);
}

public sealed class AchievementShowcaseService(IAchievementManager achievements, IAchievementSnapshotService snapshots) : IAchievementShowcaseService
{
    public void ShowDefinitions(GameClient session) =>
        session.Send(new BadgeDefinitionsComposer(snapshots.CaptureDefinitions(achievements.Achievements.Values)));

    public void ShowAchievements(GameClient session) =>
        session.Send(new AchievementsComposer(snapshots.Capture(session.GetHabbo(), achievements.Achievements.Values)));

    public void ShowGameAchievements(GameClient session, int gameId)
    {
        session.Send(new GameAccountStatusComposer(gameId));
        session.Send(new PlayableGamesComposer(gameId));
        session.Send(new GameAchievementListComposer(gameId, snapshots.Capture(session.GetHabbo(), achievements.GetGameAchievements(gameId))));
    }
}
