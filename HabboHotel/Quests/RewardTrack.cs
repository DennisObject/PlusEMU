namespace Plus.HabboHotel.Quests;

public sealed class RewardTrackLevel
{
    public RewardTrackLevel(int requiredCount, int pointsReward, bool premium)
    {
        RequiredCount = requiredCount;
        PointsReward = pointsReward;
        Premium = premium;
    }

    public int RequiredCount { get; }
    public int PointsReward { get; }
    public bool Premium { get; }
}

public sealed class RewardTrackTask
{
    public RewardTrackTask(string id, string actionType, string parameter, bool premium, int sortOrder, IReadOnlyList<RewardTrackLevel> levels)
    {
        Id = id;
        ActionType = actionType ?? "";
        Parameter = parameter ?? "";
        Premium = premium;
        SortOrder = sortOrder;
        Levels = levels.OrderBy(level => level.RequiredCount).ToList();
    }

    public string Id { get; }
    public string ActionType { get; }
    public string Parameter { get; }
    public bool Premium { get; }
    public int SortOrder { get; }
    public IReadOnlyList<RewardTrackLevel> Levels { get; }
    public int Cap => Levels.Count == 0 ? 0 : Levels.Max(level => level.RequiredCount);

    public bool IsComplete(int progress)
    {
        foreach (var level in Levels)
        {
            if (progress < level.RequiredCount)
                return false;
        }
        return true;
    }
}

public sealed class RewardTrackPrize
{
    public RewardTrackPrize(string id, int requiredPoints, int productItemTypeId, string rewardType, string extraParams, int rewardAmount, bool premium, int sortOrder)
    {
        Id = id;
        RequiredPoints = requiredPoints;
        ProductItemTypeId = productItemTypeId;
        RewardType = rewardType ?? "";
        ExtraParams = extraParams ?? "";
        RewardAmount = rewardAmount;
        Premium = premium;
        SortOrder = sortOrder;
    }

    public string Id { get; }
    public int RequiredPoints { get; }
    public int ProductItemTypeId { get; }
    public string RewardType { get; }
    public string ExtraParams { get; }
    public int RewardAmount { get; }
    public bool Premium { get; }
    public int SortOrder { get; }
}

public sealed class RewardTrack
{
    private readonly List<RewardTrackTask> _tasks = new();
    private readonly List<RewardTrackPrize> _prizes = new();

    public RewardTrack(string id, string theme, int sortOrder, int startsAt, int endsAt, bool hasPremium, double premiumTaskPointsBoost, int premiumInstantPoints, int premiumCostDiamonds, int premiumCostCredits)
    {
        Id = id;
        Theme = string.IsNullOrWhiteSpace(theme) ? "blue" : theme;
        SortOrder = sortOrder;
        StartsAt = startsAt;
        EndsAt = endsAt;
        HasPremium = hasPremium;
        PremiumTaskPointsBoost = premiumTaskPointsBoost;
        PremiumInstantPoints = premiumInstantPoints;
        PremiumCostDiamonds = premiumCostDiamonds;
        PremiumCostCredits = premiumCostCredits;
    }

    public string Id { get; }
    public string Theme { get; }
    public int SortOrder { get; }
    public int StartsAt { get; }
    public int EndsAt { get; }
    public bool HasPremium { get; }
    public double PremiumTaskPointsBoost { get; }
    public int PremiumInstantPoints { get; }
    public int PremiumCostDiamonds { get; }
    public int PremiumCostCredits { get; }
    public IReadOnlyList<RewardTrackTask> Tasks => _tasks;
    public IReadOnlyList<RewardTrackPrize> Prizes => _prizes;

    public bool IsActive(int now) => (StartsAt <= 0 || StartsAt <= now) && (EndsAt <= 0 || now < EndsAt);

    public void AddTask(RewardTrackTask task)
    {
        _tasks.Add(task);
        _tasks.Sort((left, right) =>
        {
            var order = left.SortOrder.CompareTo(right.SortOrder);
            return order != 0 ? order : string.CompareOrdinal(left.Id, right.Id);
        });
    }

    public void AddPrize(RewardTrackPrize prize)
    {
        _prizes.Add(prize);
        _prizes.Sort((left, right) =>
        {
            var points = left.RequiredPoints.CompareTo(right.RequiredPoints);
            if (points != 0)
                return points;
            var order = left.SortOrder.CompareTo(right.SortOrder);
            return order != 0 ? order : string.CompareOrdinal(left.Id, right.Id);
        });
    }

    public RewardTrackPrize? GetPrize(string prizeId)
    {
        foreach (var prize in _prizes)
        {
            if (prize.Id == prizeId)
                return prize;
        }
        return null;
    }

    /// <summary>Points for levels crossed from <paramref name="before"/> to <paramref name="after"/>. Premium levels are skipped without the pass, then the boost rounds.</summary>
    public int PointsFor(RewardTrackTask task, int before, int after, bool premiumUser)
    {
        var points = 0;
        foreach (var level in task.Levels)
        {
            if (level.Premium && !premiumUser)
                continue;
            if (before < level.RequiredCount && after >= level.RequiredCount)
                points += level.PointsReward;
        }
        if (premiumUser && PremiumTaskPointsBoost > 1)
            points = (int)Math.Round(points * PremiumTaskPointsBoost, MidpointRounding.AwayFromZero);
        return points;
    }
}
