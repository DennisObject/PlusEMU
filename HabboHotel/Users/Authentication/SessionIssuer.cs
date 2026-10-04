using Plus.HabboHotel.Moderation;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// The one place that hands out and takes back login credentials: game SSO tickets, HTTP access
/// tokens and "remember me" tokens.
/// </summary>
public interface ISessionIssuer
{
    /// <summary>A fresh single-use game ticket and an access token; with <paramref name="remember"/>
    /// also a new remember-me family. Callers have already checked the password.</summary>
    Task<AuthSession> Issue(int userId, string username, bool remember = false);

    /// <summary>
    /// Trades a remember-me token for its successor plus an access token and, when
    /// <paramref name="withTicket"/>, a game ticket. Banned users lose all remember tokens.
    /// </summary>
    Task<ResumeResult> Resume(string rememberToken, string address, bool withTicket);

    /// <summary>Signs the user out everywhere: game ticket, access tokens and remember tokens.</summary>
    Task RevokeAll(int userId);
}

/// <param name="SsoTicket">Default when the session was resumed without a ticket.</param>
public sealed record AuthSession(string Username, IssuedToken SsoTicket, IssuedToken AccessToken, IssuedToken? RememberToken = null);

public enum ResumeStatus
{
    Resumed,
    Invalid,
    Banned
}

public sealed record ResumeResult(ResumeStatus Status, AuthSession? Session = null, LoginBan? Ban = null);

public class SessionIssuer : ISessionIssuer
{
    private readonly ISsoTicketStore _ssoTickets;
    private readonly IAccessTokenStore _accessTokens;
    private readonly IRememberTokenStore _rememberTokens;
    private readonly IAccountStore _accounts;
    private readonly IBanLookup _bans;

    public SessionIssuer(ISsoTicketStore ssoTickets, IAccessTokenStore accessTokens, IRememberTokenStore rememberTokens, IAccountStore accounts, IBanLookup bans)
    {
        _ssoTickets = ssoTickets;
        _accessTokens = accessTokens;
        _rememberTokens = rememberTokens;
        _accounts = accounts;
        _bans = bans;
    }

    public async Task<AuthSession> Issue(int userId, string username, bool remember = false) =>
        new(username, await _ssoTickets.Issue(userId), await _accessTokens.Issue(userId), remember ? await _rememberTokens.Issue(userId) : null);

    public async Task<ResumeResult> Resume(string rememberToken, string address, bool withTicket)
    {
        var rotation = await _rememberTokens.Rotate(rememberToken);
        if (rotation.Status != RememberRotationStatus.Rotated)
            return new(ResumeStatus.Invalid);

        var userId = rotation.UserId;
        if (await _accounts.UsernameById(userId) is not { } username)
        {
            await _rememberTokens.RevokeAll(userId);
            return new(ResumeStatus.Invalid);
        }
        if (await _bans.Find(username, address) is { } ban)
        {
            await _rememberTokens.RevokeAll(userId);
            return new(ResumeStatus.Banned, Ban: ban);
        }

        var ssoTicket = withTicket ? await _ssoTickets.Issue(userId) : default;
        return new(ResumeStatus.Resumed, new(username, ssoTicket, await _accessTokens.Issue(userId), rotation.Token));
    }

    public async Task RevokeAll(int userId)
    {
        await _ssoTickets.Revoke(userId);
        await _accessTokens.RevokeAll(userId);
        await _rememberTokens.RevokeAll(userId);
    }
}
