using Plus.Utilities;

namespace Plus.HabboHotel.Moderation;

public class ModerationBan
{
    public ModerationBan(ModerationBanType type, string value, string reason, double expire)
    {
        Type = type;
        Value = value;
        Reason = reason;
        Expire = expire;
    }

    public string Value { get; set; }
    public double Expire { get; set; }
    public string Reason { get; set; }
    public ModerationBanType Type { get; set; }

    public bool Expired
    {
        get
        {
            if (BanClock.Now() >= Expire)
                return true;
            return false;
        }
    }
}