namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// "Remember me" tokens in user_remember_tokens: random, stored as SHA-256 digests, valid for
/// AuthApi:RememberTokenLifetimeDays. Each login starts a family; each use rotates the token
/// within it, and presenting an already-rotated token revokes the whole family.
/// </summary>
public interface IRememberTokenStore
{
    /// <summary>Starts a new family for the user (one per remembered login).</summary>
    Task<IssuedToken> Issue(int userId);

    /// <summary>Uses up the token and returns its successor.</summary>
    Task<RememberRotation> Rotate(string token);

    /// <summary>Ends the device's family the token belongs to (logout).</summary>
    Task RevokeFamily(string token);

    /// <summary>Ends every family of the user.</summary>
    Task RevokeAll(int userId);
}

public enum RememberRotationStatus
{
    Rotated,
    Invalid,
    Reused
}

public sealed record RememberRotation(RememberRotationStatus Status, int UserId = 0, IssuedToken Token = default);
