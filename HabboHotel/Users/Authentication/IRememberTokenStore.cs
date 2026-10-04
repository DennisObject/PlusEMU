namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// "Remember me" tokens in user_remember_tokens: random, stored as SHA-256 digests, valid for
/// AuthApi:RememberTokenLifetimeDays. Each login starts a family; each use rotates the token
/// within it, and presenting an already-rotated token revokes the whole family.
/// </summary>
public interface IRememberTokenStore
{
    /// <summary>Starts a new family for the user (one per remembered login).</summary>
    Task<IssuedToken> Issue(int userId, CredentialScope? scope = null);

    /// <summary>Uses the token up. A Rotated result names the family whose successor the caller
    /// writes with <see cref="Continue"/>, and the user's credential generation at that moment.</summary>
    Task<RememberRotation> Rotate(string token);

    /// <summary>Adds the next token of a family.</summary>
    Task<IssuedToken> Continue(int userId, string familyId, CredentialScope? scope = null);

    /// <summary>Ends the device's family the token belongs to (logout).</summary>
    Task RevokeFamily(string token);

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
