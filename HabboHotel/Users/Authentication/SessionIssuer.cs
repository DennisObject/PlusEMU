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

    /// <summary>A fresh single-use game ticket and an access token; with <paramref name="remember"/>
    /// also a new remember-me family. Null, issuing nothing, if the user's credentials were revoked
    /// since <paramref name="generation"/> was captured.</summary>
    Task<AuthSession?> Issue(int userId, string username, long generation, bool remember = false);

    /// <summary>
    /// Trades a remember-me token for its successor plus an access token and, when
    /// <paramref name="withTicket"/>, a game ticket. A replayed token, a ban or a deleted account
    /// revokes all of the user's credentials.
    /// </summary>
    Task<ResumeResult> Resume(string rememberToken, string address, bool withTicket);

    /// <summary>Trades a live game ticket for an access token, once per ticket. Null when the
    /// ticket is invalid, already exchanged, or the user's credentials were revoked meanwhile.</summary>
    Task<IssuedToken?> ExchangeTicket(string ticket);

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

    public async Task<AuthSession?> Issue(int userId, string username, long generation, bool remember = false)
    {
        AuthSession? session = null;
        await _generations.WriteIfCurrent(userId, generation, async scope =>
            session = new(username, await _ssoTickets.Issue(userId, scope), await _accessTokens.Issue(userId, scope),
                remember ? await _rememberTokens.Issue(userId, scope) : null));
        return session;
    }

    public async Task<ResumeResult> Resume(string rememberToken, string address, bool withTicket)
    {
        var rotation = await _rememberTokens.Rotate(rememberToken);
        if (rotation.Status == RememberRotationStatus.Reused)
        {
            // A token was used twice, so someone else holds it: the account is suspect.
            await RevokeAll(rotation.UserId);
            return new(ResumeStatus.Invalid);
        }
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

        AuthSession? session = null;
        await _generations.WriteIfCurrent(userId, rotation.Generation, async scope =>
            session = new(username, withTicket ? await _ssoTickets.Issue(userId, scope) : default, await _accessTokens.Issue(userId, scope),
                await _rememberTokens.Continue(userId, rotation.FamilyId, scope)));
        return session == null ? new(ResumeStatus.Invalid) : new(ResumeStatus.Resumed, session);
    }

    public async Task<IssuedToken?> ExchangeTicket(string ticket)
    {
        if (string.IsNullOrEmpty(ticket) || await _ssoTickets.FindUser(ticket) is not { } userId)
            return null;
        var generation = await _generations.Current(userId);
        if (await _ssoTickets.Exchange(ticket) != userId)
            return null;

        IssuedToken? token = null;
        await _generations.WriteIfCurrent(userId, generation, async scope => token = await _accessTokens.Issue(userId, scope));
        return token;
    }

    public Task RevokeAll(int userId) => _generations.Revoke(userId, async scope =>
    {
        await _ssoTickets.Revoke(userId, scope);
        await _accessTokens.RevokeAll(userId, scope);
        await _rememberTokens.RevokeAll(userId, scope);
    });
}
