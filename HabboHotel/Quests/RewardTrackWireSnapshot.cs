using System.Collections.Immutable;

namespace Plus.HabboHotel.Quests;

public sealed record RewardTrackWireLevel(int RequiredCount, int PointsReward, bool Premium);

public sealed record RewardTrackWireTask(string Id, string ActionType, string Parameter, int Progress, bool Premium, ImmutableArray<RewardTrackWireLevel> Levels);

public sealed record RewardTrackWirePrize(string Id, int RequiredPoints, int ProductItemTypeId, string RewardType, string ExtraParams, int RewardAmount, bool Premium, bool Reachable, bool Claimed);

public sealed record RewardTrackWireTrack(
    string Id,
    string Theme,
    int Points,
    bool HasPremium,
    double PremiumTaskPointsBoost,
    int PremiumInstantPoints,
    int PremiumCostDiamonds,
    int PremiumCostCredits,
    bool Premium,
    bool Complete,
    bool PremiumComplete,
    ImmutableArray<RewardTrackWireTask> Tasks,
    ImmutableArray<RewardTrackWirePrize> Prizes);

public static class RewardTrackWireSnapshot
{
    // Reads the live track and the user's state once, so the packet never sees later changes to either.
    public static RewardTrackWireTrack Capture(RewardTrack track, UserRewardTrackState state)
    {
        var complete = track.Prizes.Where(prize => !prize.Premium).All(prize => state.IsClaimed(prize.Id));
        var premiumComplete = !track.HasPremium || (complete && track.Prizes.Where(prize => prize.Premium).All(prize => state.IsClaimed(prize.Id)));
        return new RewardTrackWireTrack(
            track.Id,
            track.Theme,
            state.Points,
            track.HasPremium,
            track.PremiumTaskPointsBoost,
            track.PremiumInstantPoints,
            track.PremiumCostDiamonds,
            track.PremiumCostCredits,
            state.Premium,
            complete,
            premiumComplete,
            track.Tasks.Select(task => CaptureTask(task, state)).ToImmutableArray(),
            track.Prizes.Select(prize => CapturePrize(prize, state)).ToImmutableArray());
    }

    private static RewardTrackWireTask CaptureTask(RewardTrackTask task, UserRewardTrackState state) => new(
        task.Id,
        task.ActionType,
        task.Parameter,
        state.ProgressOf(task.Id),
        task.Premium,
        task.Levels.Select(level => new RewardTrackWireLevel(level.RequiredCount, level.PointsReward, level.Premium)).ToImmutableArray());

    private static RewardTrackWirePrize CapturePrize(RewardTrackPrize prize, UserRewardTrackState state)
    {
        var premiumLocked = prize.Premium && !state.Premium;
        return new RewardTrackWirePrize(
            prize.Id,
            prize.RequiredPoints,
            prize.ProductItemTypeId,
            prize.RewardType,
            prize.ExtraParams,
            prize.RewardAmount,
            prize.Premium,
            !premiumLocked && state.Points >= prize.RequiredPoints,
            state.IsClaimed(prize.Id));
    }
}
