using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Quests;

public interface IRewardTrackManager
{
    void Progress(GameClient session, string actionType, int amount = 1);
    void SendTracks(GameClient session);
    Task Claim(GameClient session, string trackId, string prizeId);
    void PurchasePremium(GameClient session, string trackId);
}
