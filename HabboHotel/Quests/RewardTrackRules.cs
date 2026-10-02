namespace Plus.HabboHotel.Quests;

public static class RewardTrackRules
{
    public static RewardTrackStep Plan(RewardTrack track, UserRewardTrackState state, RewardTrackTask task, int amount)
    {
        var before = state.ProgressOf(task.Id);
        if (amount < 1 || (task.Premium && !state.Premium) || task.IsComplete(before))
            return new RewardTrackStep(false, task.Id, before, state.PeakOf(task.Id), 0, state.Points);
        var after = (int)Math.Min(task.Cap, Math.Min(int.MaxValue, (long)before + amount));
        if (after == before)
            return new RewardTrackStep(false, task.Id, before, state.PeakOf(task.Id), 0, state.Points);
        var previousPeak = state.PeakOf(task.Id);
        var granted = after > previousPeak ? track.PointsFor(task, previousPeak, after, state.Premium) : 0;
        var peak = Math.Max(previousPeak, after);
        return new RewardTrackStep(true, task.Id, after, peak, granted, state.Points + granted);
    }

    public static RewardTrackStep Advance(RewardTrack track, UserRewardTrackState state, RewardTrackTask task, int amount)
    {
        var step = Plan(track, state, task, amount);
        if (step.Changed)
            state.Apply(step);
        return step;
    }

    public static int PreviewClaim(RewardTrack? track, UserRewardTrackState? state, string? prizeId)
    {
        if (track == null || state == null || string.IsNullOrEmpty(prizeId))
            return RewardTrackResults.Unknown;
        var prize = track.GetPrize(prizeId);
        if (prize == null)
            return RewardTrackResults.Unknown;
        if (state.IsClaimed(prizeId))
            return RewardTrackResults.AlreadyClaimed;
        if (prize.Premium && !state.Premium)
            return RewardTrackResults.PremiumRequired;
        if (state.Points < prize.RequiredPoints)
            return RewardTrackResults.NotEnoughPoints;
        if (!IsKnownReward(prize))
            return RewardTrackResults.Unknown;
        return RewardTrackResults.Ok;
    }

    public static bool IsKnownReward(RewardTrackPrize prize)
    {
        var type = prize.RewardType.Trim().ToLowerInvariant();
        if (type is "credits" or "duckets" or "diamonds")
            return prize.RewardAmount > 0;
        return type == "badge" && !string.IsNullOrWhiteSpace(prize.ExtraParams);
    }

    public static PremiumQuote Quote(RewardTrack? track, UserRewardTrackState? state, int credits, int diamonds)
    {
        if (track == null || state == null || !track.HasPremium)
            return new PremiumQuote(RewardTrackResults.Unknown, credits, diamonds, state?.Points ?? 0);
        if (state.Premium)
            return new PremiumQuote(RewardTrackResults.AlreadyPremium, credits, diamonds, state.Points);
        var costCredits = Math.Max(0, track.PremiumCostCredits);
        var costDiamonds = Math.Max(0, track.PremiumCostDiamonds);
        if (credits < costCredits || diamonds < costDiamonds)
            return new PremiumQuote(RewardTrackResults.NotEnoughCurrency, credits, diamonds, state.Points);
        return new PremiumQuote(RewardTrackResults.Ok, credits - costCredits, diamonds - costDiamonds, state.Points + Math.Max(0, track.PremiumInstantPoints));
    }
}
