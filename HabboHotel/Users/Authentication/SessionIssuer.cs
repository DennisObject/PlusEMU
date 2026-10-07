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

    /// <summary>
    /// Trades a live access token for a fresh game ticket and a successor access token in the token's
    /// session, e.g. to log in again after a dropped connection. The presented token is spent, and its
    /// successor expires when it would have, so renewing never extends a session. A ban or a deleted
    /// account revokes all of the user's credentials.
    /// </summary>
    Task<ResumeResult> RenewTicket(string accessToken, string address);

    /// <summary>Ends the login sessions the given credentials belong to (one device): their ticket,
    /// access tokens and remember family. Other devices stay signed in.</summary>
    Task Logout(string? accessToken, string? ssoTicket, string? rememberToken);

    /// <summary>Signs the user out everywhere (game ticket, access tokens, remember tokens) and
    /// voids any login still in flight.</summary>
    Task RevokeAll(int userId, CancellationToken cancellationToken = default);
}

/// <param name="SsoTicket">Default when the session was resumed without a ticket.</param>
public sealed record AuthSession(int UserId, string Username, IssuedToken SsoTicket, IssuedToken AccessToken, IssuedToken? RememberToken = null);

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
    private readonly TimeProvider _time;

    public SessionIssuer(ISsoTicketStore ssoTickets, IAccessTokenStore accessTokens, IRememberTokenStore rememberTokens, ICredentialGenerations generations,
        IAccountStore accounts, IBanLookup bans, TimeProvider time)
    {
        _ssoTickets = ssoTickets;
        _accessTokens = accessTokens;
        _rememberTokens = rememberTokens;
        _generations = generations;
        _accounts = accounts;
        _bans = bans;
        _time = time;
    }

    public Task<long> Generation(int userId) => _generations.Current(userId);

    public async Task<AuthSession?> Issue(int userId, string username, long generation, string address, bool remember = false)
    {
        var instant = CredentialInstant.Capture(_time);
        var sessionId = CredentialGenerations.NewSessionId();
        AuthSession? session = null;
        await _generations.WriteIfCurrent(userId, generation, async scope =>
        {
            await _generations.StartSessionAt(userId, sessionId, instant, scope);
            await _accounts.RecordAddress(userId, address, scope);
            session = new(userId, username, await _ssoTickets.IssueAt(userId, sessionId, instant, scope),
                await _accessTokens.IssueAt(userId, sessionId, instant, scope),
                remember ? await _rememberTokens.ContinueAt(userId, sessionId, instant, scope) : null);
        });

        return session;
    }

    public async Task<ResumeResult> Resume(string rememberToken, string address, bool withTicket)
    {
        var instant = CredentialInstant.Capture(_time);
        // A live token used twice means someone else holds it: the account is suspect, so it is
        // signed out everywhere in the same transaction that detects the reuse.
        var rotation = await _rememberTokens.RotateAt(rememberToken, instant, RevokeEverything);

        if (rotation.Status != RememberRotationStatus.Rotated) {
            return new(ResumeStatus.Invalid);
        }

        var userId = rotation.UserId;

        if (await _accounts.UsernameById(userId) is not { } username) {
            await RevokeAll(userId);

            return new(ResumeStatus.Invalid);
        }

        if (await _bans.FindAt(username, address, instant.UtcNow) is { } ban) {
            await RevokeAll(userId);

            return new(ResumeStatus.Banned, Ban: ban);
        }

        var sessionId = rotation.FamilyId;
        AuthSession? session = null;
        await _generations.WriteInSession(userId, rotation.Generation, sessionId, async scope =>
        {
            await _accounts.RecordAddress(userId, address, scope);
            session = new(userId, username, withTicket ? await _ssoTickets.IssueAt(userId, sessionId, instant, scope) : default,
                await _accessTokens.IssueAt(userId, sessionId, instant, scope),
                await _rememberTokens.ContinueAt(userId, sessionId, instant, scope));
        });

        return session == null ? new(ResumeStatus.Invalid) : new(ResumeStatus.Resumed, session);
    }

    public async Task<IssuedToken?> ExchangeTicket(string ticket)
    {
        var instant = CredentialInstant.Capture(_time);

        if (string.IsNullOrEmpty(ticket) || await _ssoTickets.FindUserAt(ticket, instant) is not { } userId) {
            return null;
        }

        var generation = await _generations.Current(userId);

        // Exchange always hands back a session (it gives a CMS-written ticket one), so logout can end it.
        if (await _ssoTickets.ExchangeAt(ticket, instant) is not { SessionId: { } sessionId } owner || owner.UserId != userId) {
            return null;
        }

        IssuedToken? token = null;
        await _generations.WriteInSession(userId, generation, sessionId,
            async scope => token = await _accessTokens.IssueAt(userId, sessionId, instant, scope));

        return token;
    }

    public async Task<ResumeResult> RenewTicket(string accessToken, string address)
    {
        var instant = CredentialInstant.Capture(_time);

        if (string.IsNullOrEmpty(accessToken) || await _accessTokens.FindOwner(accessToken) is not { SessionId: { } sessionId } owner) {
            return new(ResumeStatus.Invalid);
        }

        var userId = owner.UserId;

        if (await _accounts.UsernameById(userId) is not { } username) {
            await RevokeAll(userId);

            return new(ResumeStatus.Invalid);
        }

        if (await _bans.FindAt(username, address, instant.UtcNow) is { } ban) {
            await RevokeAll(userId);

            return new(ResumeStatus.Banned, Ban: ban);
        }

        // A revoke landing after this read bumps the generation or revokes the token; either way the
        // locked write below issues nothing.
        var generation = await _generations.Current(userId);
        AuthSession? session = null;
        await _generations.WriteInSession(userId, generation, sessionId, async scope =>
        {
            if (await _accessTokens.SpendAt(accessToken, instant, scope) is not { } expiresAt) {
                return;
            }

            await _accounts.RecordAddress(userId, address, scope);
            session = new(userId, username, await _ssoTickets.IssueAt(userId, sessionId, instant, scope),
                await _accessTokens.IssueAt(userId, sessionId, instant, scope, expiresAt));
        });

        return session == null ? new(ResumeStatus.Invalid) : new(ResumeStatus.Resumed, session);
    }

    public async Task Logout(string? accessToken, string? ssoTicket, string? rememberToken)
    {
        // A ticket's session can change under us (an exchange tags a CMS ticket with a new one), so
        // the pre-lock read only names the user to lock; the ticket is withdrawn and whatever session
        // it carries at that moment is ended in one transaction under the lock Exchange also takes.
        if (!string.IsNullOrEmpty(ssoTicket) && await _ssoTickets.FindOwner(ssoTicket) is { } byTicket) {
            await _generations.Locked(byTicket.UserId, async scope =>
            {
                if (await _ssoTickets.Withdraw(byTicket.UserId, ssoTicket, scope) is { SessionId: { } sessionId }) {
                    await EndSession(byTicket.UserId, sessionId, scope);
                }
            });
        }

        // Access tokens and remember families never change session, so their owners stay valid.
        var sessions = new HashSet<CredentialOwner>();

        if (!string.IsNullOrEmpty(accessToken) && await _accessTokens.FindOwner(accessToken) is { } byToken) {
            sessions.Add(byToken);
        }

        if (!string.IsNullOrEmpty(rememberToken) && await _rememberTokens.FindOwner(rememberToken) is { } byRemember) {
            sessions.Add(byRemember);
        }

        foreach (var (userId, sessionId) in sessions) {
            if (sessionId != null) {
                await _generations.Locked(userId, scope => EndSession(userId, sessionId, scope));
            }
        }

        // Tokens without a session still end with their own logout.
        if (!string.IsNullOrEmpty(accessToken)) {
            await _accessTokens.Revoke(accessToken);
        }
    }

    public Task RevokeAll(int userId, CancellationToken cancellationToken = default) =>
        _generations.Revoke(userId, scope => RevokeCredentials(userId, scope), cancellationToken);

    private async Task EndSession(int userId, string sessionId, CredentialScope scope)
    {
        await _generations.MarkSessionRevoked(userId, sessionId, scope);
        await _ssoTickets.RevokeSession(userId, sessionId, scope);
        await _accessTokens.RevokeSession(sessionId, scope);
        await _rememberTokens.RevokeSession(sessionId, scope);
    }

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
