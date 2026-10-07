namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// HTTP bearer tokens in user_access_tokens. Only SHA-256 digests are stored; tokens
/// expire after AuthApi:AccessTokenLifetimeMinutes and can be revoked.
/// </summary>
public interface IAccessTokenStore
{
    /// <param name="sessionId">The login session the token belongs to; logout revokes per session.</param>
    /// <param name="scope">Joins a credential transaction instead of using a connection of its own.</param>
    Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null);
    /// <param name="notAfter">Caps the expiry, so a successor never outlives the token it replaces.</param>
    Task<IssuedToken> IssueAt(int userId, string? sessionId, CredentialInstant instant, CredentialScope? scope = null, DateTimeOffset? notAfter = null);

    /// <summary>The owner of a live (unexpired, unrevoked) token.</summary>
    Task<int?> FindUser(string token);

    /// <summary>The owner and session of a live token.</summary>
    Task<CredentialOwner?> FindOwner(string token);

    Task Revoke(string token);

    /// <summary>Revokes a live token inside a caller's transaction that holds the owner's row lock and
    /// returns its expiry; null when the token was not live (so only one caller can spend it).</summary>
    Task<DateTimeOffset?> SpendAt(string token, CredentialInstant instant, CredentialScope scope);

    /// <summary>Signs a user out of every HTTP session, e.g. after a password change.</summary>
    Task RevokeAll(int userId, CredentialScope? scope = null);

    /// <summary>Revokes every token of one login session.</summary>
    Task RevokeSession(string sessionId, CredentialScope scope);

    /// <summary>Deletes up to <paramref name="batch"/> tokens that expired before <paramref name="cutoff"/>.</summary>
    Task<int> Prune(DateTimeOffset cutoff, int batch);
}
