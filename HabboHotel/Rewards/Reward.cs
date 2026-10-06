namespace Plus.HabboHotel.Rewards;

public sealed class Reward
{
    public Reward(DateTimeOffset? startsAt, DateTimeOffset? endsAt, string type, string rewardData, string message)
    {
        StartsAt = startsAt?.ToUniversalTime();
        EndsAt = endsAt?.ToUniversalTime();
        Type = RewardTypeUtility.GetType(type);
        RewardData = rewardData;
        Message = message;
    }

    public DateTimeOffset? StartsAt { get; }
    public DateTimeOffset? EndsAt { get; }
    public RewardType Type { get; }
    public string RewardData { get; }
    public string Message { get; }

    public bool IsActiveAt(DateTimeOffset utcNow) =>
        StartsAt is { } start && EndsAt is { } end && utcNow.ToUniversalTime() >= start && utcNow.ToUniversalTime() <= end;
}
