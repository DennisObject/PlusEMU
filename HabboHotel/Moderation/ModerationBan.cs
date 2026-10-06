namespace Plus.HabboHotel.Moderation;

public class ModerationBan
{
    public ModerationBan(ModerationBanType type, string value, string reason, DateTimeOffset? expiresAt)
    {
        Type = type;
        Value = value;
        Reason = reason;
        ExpiresAt = expiresAt?.ToUniversalTime();
    }

    public string Value { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string Reason { get; set; }
    public ModerationBanType Type { get; set; }

    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt is not { } expiresAt || expiresAt <= now;
}
