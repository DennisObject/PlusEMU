namespace Plus.HabboHotel.Subscriptions;

/// <summary>Immutable Habbo Club state. Instants are UTC; durations and counts stay numeric.</summary>
public sealed record ClubMembership
{
    internal const string Columns = "expires_at AS ExpiresAt, started_at AS StartedAt, first_started_at AS FirstStartedAt, past_seconds AS PastSeconds, modified_at AS ModifiedAt, gifts_claimed AS GiftsClaimed";
    public const long Day = 86400;
    public const long Period = 31 * Day;

    public ClubMembership(DateTimeOffset? expiresAt = null, DateTimeOffset? startedAt = null, DateTimeOffset? firstStartedAt = null,
        long pastSeconds = 0, DateTimeOffset? modifiedAt = null, int giftsClaimed = 0)
    {
        ExpiresAt = ToUtc(expiresAt);
        StartedAt = ToUtc(startedAt);
        FirstStartedAt = ToUtc(firstStartedAt);
        PastSeconds = pastSeconds;
        ModifiedAt = ToUtc(modifiedAt);
        GiftsClaimed = giftsClaimed;
    }

    public DateTimeOffset? ExpiresAt
    {
        get;
    }
    public DateTimeOffset? StartedAt
    {
        get;
    }
    public DateTimeOffset? FirstStartedAt
    {
        get;
    }
    public long PastSeconds
    {
        get; init;
    }
    public DateTimeOffset? ModifiedAt
    {
        get;
    }
    public int GiftsClaimed
    {
        get; init;
    }

    public static ClubMembership None { get; } = new();
    public bool Active(DateTimeOffset now) => ExpiresAt is { } expiry && now < expiry;
    public long SecondsLeft(DateTimeOffset now) => ExpiresAt is { } expiry ? WholeSeconds(expiry - now) : 0;

    /// <summary>Whole seconds since the modification instant; zero when it is unknown.</summary>
    public long SecondsSinceModified(DateTimeOffset now) => ModifiedAt is { } modified ? WholeSeconds(now - modified) : 0;

    /// <summary>Past tenure plus the running interval, capped at expiry. A missing expiry contributes no running time.</summary>
    public long Elapsed(DateTimeOffset now)
    {
        if (StartedAt is not { } started || ExpiresAt is not { } expiry)
        {
            return PastSeconds;
        }

        var end = expiry < now ? expiry : now;

        return PastSeconds + WholeSeconds(end - started);
    }

    public int EarnedGifts(DateTimeOffset now) => (int)Math.Min(int.MaxValue, (Elapsed(now) + Period - 1) / Period);
    public int AvailableGifts(DateTimeOffset now) => Math.Max(0, EarnedGifts(now) - GiftsClaimed);

    /// <summary>Whole seconds in a duration; negative spans are zero.</summary>
    public static long WholeSeconds(TimeSpan span) => Math.Max(0, span.Ticks) / TimeSpan.TicksPerSecond;

    /// <summary>Extends from the later of now and the current expiry; returns null when the result is outside DateTimeOffset.</summary>
    public static DateTimeOffset? Extend(DateTimeOffset now, DateTimeOffset? expiry, int days)
    {
        var basis = ToUtc(expiry) is { } current && current > now ? current : ToUtc(now)!.Value;

        // Divide rather than multiply: a product of a large day count and ticks-per-day overflows long.
        if (days < 0 || days > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerDay)
        {
            return null;
        }

        var span = new TimeSpan((long)days * TimeSpan.TicksPerDay);

        // The exact remaining range is compared before adding, so no value outside DateTimeOffset is ever constructed.
        return span > DateTimeOffset.MaxValue - basis ? null : basis + span;
    }

    private static DateTimeOffset? ToUtc(DateTimeOffset? instant) => instant?.ToUniversalTime();
}

/// <summary>Database projection with property setters; the registered UTC handler materializes each instant.</summary>
internal sealed class ClubMembershipRow
{
    public DateTimeOffset? ExpiresAt
    {
        get; set;
    }
    public DateTimeOffset? StartedAt
    {
        get; set;
    }
    public DateTimeOffset? FirstStartedAt
    {
        get; set;
    }
    public long PastSeconds
    {
        get; set;
    }
    public DateTimeOffset? ModifiedAt
    {
        get; set;
    }
    public int GiftsClaimed
    {
        get; set;
    }

    public ClubMembership ToMembership() => new(ExpiresAt, StartedAt, FirstStartedAt, PastSeconds, ModifiedAt, GiftsClaimed);
}
