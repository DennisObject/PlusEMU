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

    /// <summary>The owner of a live (unexpired, unrevoked) token.</summary>
    Task<int?> FindUser(string token);

    /// <summary>The owner and session of a live token.</summary>
    Task<CredentialOwner?> FindOwner(string token);

    Task Revoke(string token);

    /// <summary>Signs a user out of every HTTP session, e.g. after a password change.</summary>
    Task RevokeAll(int userId, CredentialScope? scope = null);

    /// <summary>Revokes every token of one login session.</summary>
    Task RevokeSession(string sessionId, CredentialScope scope);

    /// <summary>Deletes up to <paramref name="batch"/> tokens that expired before <paramref name="cutoff"/>.</summary>
    Task<int> Prune(long cutoff, int batch);
}
