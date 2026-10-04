using Plus.HabboHotel.Moderation;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// The one place that hands out and takes back login credentials: game SSO tickets, HTTP access
/// tokens and "remember me" tokens. Every write happens under the user's credential generation,
/// so RevokeAll (password reset, ban, detected theft) is never outlived by a login in flight.
/// </summary>
public interface ISessionIssuer
{
    /// <summary>Capture before checking a password; pass it to <see cref="Issue"/>.</summary>
    Task<long> Generation(int userId);

    /// <summary>Starts a login session: a fresh single-use game ticket and an access token, and with
    /// <paramref name="remember"/> a remember-me family; records <paramref name="address"/> as the
    /// user's last IP. Null, issuing nothing, if the user's credentials were revoked since
    /// <paramref name="generation"/> was captured.</summary>
    Task<AuthSession?> Issue(int userId, string username, long generation, string address, bool remember = false);

    /// <summary>
    /// Trades a remember-me token for its successor plus an access token and, when
    /// <paramref name="withTicket"/>, a game ticket, within the token's session. A replayed live
    /// token, a ban or a deleted account revokes all of the user's credentials.
    /// </summary>
    Task<ResumeResult> Resume(string rememberToken, string address, bool withTicket);

    /// <summary>Trades a live game ticket for an access token in the ticket's session, once per
    /// ticket. Null when the ticket is invalid, already exchanged, or its session or the user's
    /// credentials were revoked meanwhile.</summary>
    Task<IssuedToken?> ExchangeTicket(string ticket);

    /// <summary>Ends the login sessions the given credentials belong to (one device): their ticket,
    /// access tokens and remember family. Other devices stay signed in.</summary>
    Task Logout(string? accessToken, string? ssoTicket, string? rememberToken);

    /// <summary>Signs the user out everywhere (game ticket, access tokens, remember tokens) and
    /// voids any login still in flight.</summary>
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
    private readonly ICredentialGenerations _generations;
    private readonly IAccountStore _accounts;
    private readonly IBanLookup _bans;

    public SessionIssuer(ISsoTicketStore ssoTickets, IAccessTokenStore accessTokens, IRememberTokenStore rememberTokens, ICredentialGenerations generations,
        IAccountStore accounts, IBanLookup bans)
    {
        _ssoTickets = ssoTickets;
        _accessTokens = accessTokens;
        _rememberTokens = rememberTokens;
        _generations = generations;
        _accounts = accounts;
        _bans = bans;
    }

    public Task<long> Generation(int userId) => _generations.Current(userId);

    public async Task<AuthSession?> Issue(int userId, string username, long generation, string address, bool remember = false)
    {
        var sessionId = CredentialGenerations.NewSessionId();
        AuthSession? session = null;
        await _generations.WriteIfCurrent(userId, generation, null, async scope =>
        {
            await _generations.StartSession(userId, sessionId, scope);
            session = new(username, await _ssoTickets.Issue(userId, sessionId, scope), await _accessTokens.Issue(userId, sessionId, scope),
                remember ? await _rememberTokens.Continue(userId, sessionId, scope) : null);
        });
        return session;
    }

    public async Task<ResumeResult> Resume(string rememberToken, string address, bool withTicket)
    {
        // A live token used twice means someone else holds it: the account is suspect, so it is
        // signed out everywhere in the same transaction that detects the reuse.
        var rotation = await _rememberTokens.Rotate(rememberToken, RevokeEverything);
        if (rotation.Status != RememberRotationStatus.Rotated)
            return new(ResumeStatus.Invalid);

        var userId = rotation.UserId;
        if (await _accounts.UsernameById(userId) is not { } username)
        {
            await RevokeAll(userId);
            return new(ResumeStatus.Invalid);
        }
        if (await _bans.Find(username, address) is { } ban)
        {
            await RevokeAll(userId);
            return new(ResumeStatus.Banned, Ban: ban);
        }

        var sessionId = rotation.FamilyId;
        AuthSession? session = null;
        await _generations.WriteIfCurrent(userId, rotation.Generation, sessionId, async scope =>
        {
            session = new(username, withTicket ? await _ssoTickets.Issue(userId, sessionId, scope) : default,
                await _accessTokens.Issue(userId, sessionId, scope), await _rememberTokens.Continue(userId, sessionId, scope));
        });
        return session == null ? new(ResumeStatus.Invalid) : new(ResumeStatus.Resumed, session);
    }

    public async Task<IssuedToken?> ExchangeTicket(string ticket)
    {
        if (string.IsNullOrEmpty(ticket) || await _ssoTickets.FindUser(ticket) is not { } userId)
            return null;
        var generation = await _generations.Current(userId);
        if (await _ssoTickets.Exchange(ticket) is not { } owner || owner.UserId != userId)
            return null;

        IssuedToken? token = null;
        await _generations.WriteIfCurrent(userId, generation, owner.SessionId, async scope => token = await _accessTokens.Issue(userId, owner.SessionId, scope));
        return token;
    }

    public async Task Logout(string? accessToken, string? ssoTicket, string? rememberToken)
    {
        var sessions = new HashSet<CredentialOwner>();
        if (!string.IsNullOrEmpty(accessToken) && await _accessTokens.FindOwner(accessToken) is { } byToken)
            sessions.Add(byToken);
        if (!string.IsNullOrEmpty(ssoTicket) && await _ssoTickets.FindOwner(ssoTicket) is { } byTicket)
        {
            sessions.Add(byTicket);
            // A ticket written outside a session (e.g. by a CMS) is simply used up.
            if (byTicket.SessionId == null)
                await _ssoTickets.Consume(ssoTicket);
        }
        if (!string.IsNullOrEmpty(rememberToken) && await _rememberTokens.FindOwner(rememberToken) is { } byRemember)
            sessions.Add(byRemember);

        foreach (var (userId, sessionId) in sessions)
        {
            if (sessionId == null)
                continue;
            await _generations.RevokeSession(userId, sessionId, async scope =>
            {
                await _ssoTickets.RevokeSession(userId, sessionId, scope);
                await _accessTokens.RevokeSession(sessionId, scope);
                await _rememberTokens.RevokeSession(sessionId, scope);
            });
        }
        // Tokens without a session still end with their own logout.
        if (!string.IsNullOrEmpty(accessToken))
            await _accessTokens.Revoke(accessToken);
    }

    public Task RevokeAll(int userId) => _generations.Revoke(userId, scope => RevokeCredentials(userId, scope));

    private async Task RevokeEverything(int userId, CredentialScope scope)
    {
        await _generations.Bump(userId, scope);
        await RevokeCredentials(userId, scope);
    }

    private async Task RevokeCredentials(int userId, CredentialScope scope)
    {
        await _ssoTickets.Revoke(userId, scope);
        await _accessTokens.RevokeAll(userId, scope);
        await _rememberTokens.RevokeAll(userId, scope);
    }
}
