using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Subscriptions;

public sealed record ClubStatusSnapshot(
    int DaysLeft,
    int PeriodsElapsed,
    int MonthsAhead,
    int ResponseType,
    bool HasEverStarted,
    int ElapsedDays,
    int MinutesLeft,
    int MinutesSinceModified)
{
    public const int InfoResponse = 1, PurchaseResponse = 2, ExpiringResponse = 3;

    public static ClubStatusSnapshot Capture(UserAccess access, int responseType = InfoResponse)
    {
        var snapshot = access.Capture(out var now);

        return Capture(snapshot.Membership, now, responseType);
    }

    public static ClubStatusSnapshot Capture(ClubMembership membership, DateTimeOffset now,
        int responseType = InfoResponse)
    {
        var seconds = membership.SecondsLeft(now);
        var days = (int)Math.Min(int.MaxValue, (seconds + ClubMembership.Day - 1) / ClubMembership.Day);
        var ahead = Math.Max(0, days - 1) / 31;
        var elapsed = membership.Elapsed(now);

        return new(days - ahead * 31,
            (int)Math.Min(int.MaxValue, elapsed / ClubMembership.Period), ahead, responseType,
            membership.FirstStartedAt is not null,
            (int)Math.Min(int.MaxValue, elapsed / ClubMembership.Day),
            (int)Math.Min(int.MaxValue, seconds / 60),
            (int)Math.Min(int.MaxValue, membership.SecondsSinceModified(now) / 60));
    }
}
