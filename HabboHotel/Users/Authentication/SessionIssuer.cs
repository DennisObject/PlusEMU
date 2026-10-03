namespace Plus.HabboHotel.Users.Authentication;

public interface ISessionIssuer
{
    /// <summary>A fresh single-use game ticket plus an HTTP access token for the user.</summary>
    Task<AuthSession> Issue(int userId, string username);
}

public sealed record AuthSession(string Username, IssuedToken SsoTicket, IssuedToken AccessToken);

public class SessionIssuer : ISessionIssuer
{
    private readonly ISsoTicketStore _ssoTickets;
    private readonly IAccessTokenStore _accessTokens;

    public SessionIssuer(ISsoTicketStore ssoTickets, IAccessTokenStore accessTokens)
    {
        _ssoTickets = ssoTickets;
        _accessTokens = accessTokens;
    }

    public async Task<AuthSession> Issue(int userId, string username) =>
        new(username, await _ssoTickets.Issue(userId), await _accessTokens.Issue(userId));
}
