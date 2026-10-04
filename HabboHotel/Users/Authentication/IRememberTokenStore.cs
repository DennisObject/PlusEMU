namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// "Remember me" tokens in user_remember_tokens: random, stored as SHA-256 digests, valid for
/// AuthApi:RememberTokenLifetimeDays. Each login starts a family; each use rotates the token
/// within it, and presenting an already-rotated token revokes the whole family.
/// </summary>
public interface IRememberTokenStore
{
    /// <summary>Starts a new remembered login session for the user (its id is the family id).</summary>
    Task<IssuedToken> Issue(int userId, CredentialScope? scope = null);

    /// <summary>
    /// Uses the token up. A Rotated result names the family whose successor the caller writes with
    /// <see cref="Continue"/>, and the user's credential generation at that moment. Revoked or
    /// expired tokens are Invalid with no side effects. A used, still live token presented again
    /// within AuthApi:RememberReuseGraceSeconds (a few times at most) is Rotated again, so its
    /// family gets another successor. Any other used, still live token is reuse: its
    /// family is revoked and <paramref name="onReuse"/> runs in the same transaction, which holds
    /// the user's row lock, so nothing commits unless it succeeds.
    /// </summary>
    Task<RememberRotation> Rotate(string token, Func<int, CredentialScope, Task>? onReuse = null);

    /// <summary>Adds the next token of a family.</summary>
    Task<IssuedToken> Continue(int userId, string familyId, CredentialScope? scope = null);

    /// <summary>The user and session (family) any token of a family belongs to.</summary>
    Task<CredentialOwner?> FindOwner(string token);

    /// <summary>Revokes every token of one session's family.</summary>
    Task RevokeSession(string sessionId, CredentialScope scope);

    /// <summary>Ends every family of the user.</summary>
    Task RevokeAll(int userId, CredentialScope? scope = null);

    /// <summary>Deletes up to <paramref name="batch"/> rows that expired before <paramref name="cutoff"/>.</summary>
    Task<int> Prune(long cutoff, int batch);
}

public enum RememberRotationStatus
{
    Rotated,
    Invalid,
    Reused
}

public sealed record RememberRotation(RememberRotationStatus Status, int UserId = 0, string FamilyId = "", long Generation = 0);
