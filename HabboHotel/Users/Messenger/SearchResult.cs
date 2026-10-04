namespace Plus.HabboHotel.Users.Messenger;

public struct SearchResult
{
    public int UserId;
    public string Username;
    public string Motto;
    public string Figure;
    public DateTimeOffset? LastOnlineAt;
    [Obsolete("Encode LastOnlineAt at the protocol boundary")]
    public string LastOnline => LastOnlineAt?.ToUnixTimeSeconds().ToString() ?? "0";

    public SearchResult(int userId, string username, string motto, string figure, DateTimeOffset? lastOnlineAt)
    {
        UserId = userId;
        Username = username;
        Motto = motto;
        Figure = figure;
        LastOnlineAt = lastOnlineAt;
    }
}
