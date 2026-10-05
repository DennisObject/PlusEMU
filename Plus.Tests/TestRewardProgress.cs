using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Tests;

internal sealed class TestRewardProgress(Action<GameClient, string, int>? beforeProgress = null) : IRewardTrackManager
{
    public static IRewardTrackManager Unused { get; } = new TestRewardProgress((_, _, _) =>
        throw new InvalidOperationException("Unexpected reward progress."));
    public List<(GameClient Session, string Action, int Amount)> Calls { get; } = [];
    public void Progress(GameClient session, string actionType, int amount = 1)
    {
        beforeProgress?.Invoke(session, actionType, amount);
        Calls.Add((session, actionType, amount));
    }
    public void SendTracks(GameClient session) => throw new NotSupportedException();
    public Task Claim(GameClient session, string trackId, string prizeId) => throw new NotSupportedException();
    public void PurchasePremium(GameClient session, string trackId) => throw new NotSupportedException();
}
