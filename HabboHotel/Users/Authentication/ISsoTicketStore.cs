namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// Game login tickets kept in users.auth_ticket. A ticket is random, expires after
/// AuthApi:SsoTicketLifetimeSeconds, matches case-exactly, belongs to one login session and can
/// log in once.
/// </summary>
public interface ISsoTicketStore
{
    /// <summary>Replaces the user's ticket with a fresh one for <paramref name="sessionId"/>.</summary>
    /// <param name="scope">Joins a credential transaction instead of using a connection of its own.</param>
    Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null);

    /// <summary>The owner of a live ticket, without using it up.</summary>
    Task<int?> FindUser(string ticket);

    /// <summary>The owner and session of a live ticket, without using it up.</summary>
    Task<CredentialOwner?> FindOwner(string ticket);

    /// <summary>Uses up a live ticket. Of several concurrent callers only one gets the user id.</summary>
    Task<int?> Consume(string ticket);

    /// <summary>
    /// Marks a live ticket as traded for an access token. Each ticket can be exchanged once; the
    /// game login can still redeem it afterwards. A ticket written without a session (e.g. by a
    /// CMS) is given one in the same users-row-locked transaction, so the owner always has one.
    /// </summary>
    Task<CredentialOwner?> Exchange(string ticket);

    /// <summary>Clears whatever ticket the user still holds.</summary>
    Task Revoke(int userId, CredentialScope? scope = null);

    /// <summary>Clears the user's ticket if it belongs to <paramref name="sessionId"/>.</summary>
    Task RevokeSession(int userId, string sessionId, CredentialScope scope);
}
