namespace Plus.HabboHotel.Subscriptions;

public sealed record ClubMembership(long ExpiresAt = 0, long StartedAt = 0, long FirstStartedAt = 0,
    long PastSeconds = 0, long ModifiedAt = 0, int GiftsClaimed = 0)
{
    internal const string Columns = "expires_at AS ExpiresAt, started_at AS StartedAt, first_started_at AS FirstStartedAt, past_seconds AS PastSeconds, modified_at AS ModifiedAt, gifts_claimed AS GiftsClaimed";
    public const long Day = 86400;
    public const long Period = 31 * Day;
    public static ClubMembership None { get; } = new();
    public bool Active(long now) => now < ExpiresAt;
    public long SecondsLeft(long now) => Math.Max(0, ExpiresAt - now);
    public long Elapsed(long now) => PastSeconds + (StartedAt > 0 ? Math.Max(0, Math.Min(now, ExpiresAt) - StartedAt) : 0);
    public int EarnedGifts(long now) => (int)Math.Min(int.MaxValue, (Elapsed(now) + Period - 1) / Period);
    public int AvailableGifts(long now) => Math.Max(0, EarnedGifts(now) - GiftsClaimed);
    public static long Extend(long now, long expiry, int days) => checked(Math.Max(now, expiry) + (long)days * Day);
}
