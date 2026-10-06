namespace Plus.HabboHotel.Rooms;

public class RoomPromotion
{
    private readonly TimeProvider _clock;

    public RoomPromotion(string name, string description, int categoryId, DateTimeOffset startedAt, DateTimeOffset expiresAt, TimeProvider clock)
        : this(name, description, (DateTimeOffset?)startedAt, expiresAt, categoryId, clock) { }

    public RoomPromotion(string name, string description, DateTimeOffset? startedAt, DateTimeOffset? expiresAt, int categoryId, TimeProvider clock)
    {
        Name = name;
        Description = description;
        StartedAt = startedAt?.ToUniversalTime();
        ExpiresAt = expiresAt?.ToUniversalTime();
        CategoryId = categoryId;
        _clock = clock;
    }

    public string Name { get; set; }

    public string Description { get; set; }

    public DateTimeOffset? StartedAt { get; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool HasExpired => HasExpiredAt(_clock.GetUtcNow());

    public int MinutesLeft => MinutesLeftAt(_clock.GetUtcNow());

    public int CategoryId { get; set; }

    public void Extend(TimeSpan duration) => ExpiresAt = (ExpiresAt ?? _clock.GetUtcNow()) + duration;

    internal bool HasExpiredAt(DateTimeOffset now) => ExpiresAt is not { } expiresAt || expiresAt <= now;

    internal int MinutesLeftAt(DateTimeOffset now)
    {
        if (HasExpiredAt(now)) {
            return 0;
        }

        return (int)Math.Min(int.MaxValue, Math.Ceiling((ExpiresAt!.Value - now).TotalMinutes));
    }
}
