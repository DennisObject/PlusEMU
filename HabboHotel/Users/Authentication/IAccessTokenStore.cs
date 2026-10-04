namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// HTTP bearer tokens in user_access_tokens. Only SHA-256 digests are stored; tokens
/// expire after AuthApi:AccessTokenLifetimeMinutes and can be revoked.
/// </summary>
public interface IAccessTokenStore
{
    /// <param name="scope">Joins a credential transaction instead of using a connection of its own.</param>
    Task<IssuedToken> Issue(int userId, CredentialScope? scope = null);

    /// <summary>The owner of a live (unexpired, unrevoked) token.</summary>
    Task<int?> FindUser(string token);

    Task Revoke(string token);

    /// <summary>Signs a user out of every HTTP session, e.g. after a password change.</summary>
    Task RevokeAll(int userId, CredentialScope? scope = null);
}
