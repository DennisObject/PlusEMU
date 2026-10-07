using Plus.Communication.Packets.Outgoing.Talents;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Talents;

public interface ITalentTrackPresentationService
{
    void ShowLevels(GameClient session, string type);
}

public sealed class TalentTrackPresentationService(ITalentTrackManager talents, IAchievementManager achievements, ITalentTrackProgressionService progression) : ITalentTrackPresentationService
{
    public void ShowLevels(GameClient session, string type)
    {
        if (type is not ("citizenship" or "helper")) {
            return;
        }
        var habbo = session.GetHabbo();
        progression.Progress(habbo, achievements.Achievements);
        lock (habbo.WalletSync) {
            if (!habbo.WalletClosed) {
                session.Send(new TalentTrackComposer(type, TalentTrackSnapshot.Capture(talents.GetLevels().Where(level => level.Type == type), habbo, achievements.Achievements)));
            }
        }
    }
}
