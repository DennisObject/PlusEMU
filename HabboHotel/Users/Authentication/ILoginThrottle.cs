namespace Plus.HabboHotel.Users.Authentication;

public interface ILoginThrottle
{
    /// <summary>How long the name or address stays locked; zero when it is not locked.</summary>
    TimeSpan BlockedFor(string username, string address);
    void RecordFailure(string username, string address);
    void RecordSuccess(string username);
}
