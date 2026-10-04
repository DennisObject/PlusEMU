namespace Plus.HabboHotel.Users.Authentication;

public interface ILoginThrottle
{
    /// <summary>How long the account or address stays locked; zero when it is not locked.
    /// <paramref name="accountKey"/> comes from LoginThrottle.AccountKey or UnknownNameKey.</summary>
    TimeSpan BlockedFor(string accountKey, string address);
    void RecordFailure(string accountKey, string address);
    void RecordSuccess(string accountKey);
}
