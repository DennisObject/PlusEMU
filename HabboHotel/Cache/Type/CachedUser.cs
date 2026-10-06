namespace Plus.HabboHotel.Cache.Type;

public sealed class CachedUser
{
    public int Id
    {
        get; init;
    }
    public string Username { get; init; } = string.Empty;
    public string Motto { get; init; } = string.Empty;
    public string Look { get; init; } = string.Empty;
    public DateTimeOffset RefreshedAt
    {
        get; init;
    }

    public bool IsExpiredAt(DateTimeOffset now) => now - RefreshedAt >= TimeSpan.FromMinutes(30);

    internal CachedUser RefreshAt(DateTimeOffset now) => new()
    {
        Id = Id,
        Username = Username,
        Motto = Motto,
        Look = Look,
        RefreshedAt = now
    };
}
