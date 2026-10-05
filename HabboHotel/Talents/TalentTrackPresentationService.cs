using Plus.Communication.Packets.Outgoing.Talents;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Talents;

public interface ITalentTrackPresentationService
{
    void ShowLevels(GameClient session, string type);
}

public sealed class TalentTrackPresentationService(ITalentTrackManager talents) : ITalentTrackPresentationService
{
    public void ShowLevels(GameClient session, string type) =>
        session.Send(new TalentTrackComposer(type, TalentTrackSnapshot.Capture(talents.GetLevels())));
}
