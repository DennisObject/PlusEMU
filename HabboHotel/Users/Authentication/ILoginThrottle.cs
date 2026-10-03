namespace Plus.HabboHotel.Users.Authentication;

public interface ILoginThrottle
{
    bool IsBlocked(string username, string address);
    void RecordFailure(string username, string address);
    void RecordSuccess(string username);
}
