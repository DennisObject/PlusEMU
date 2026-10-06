using System.Security.Cryptography;
using System.Text;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// Random bearer secrets (SSO tickets, access tokens) and the digest stored for them.
/// </summary>
public static class SecureToken
{
    private const int TokenBytes = 32;

    /// <summary>256 random bits as unpadded base64url (43 characters).</summary>
    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Lowercase SHA-256 hex, identical to MySQL/MariaDB SHA2(token, 256).</summary>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public readonly record struct IssuedToken(string Value, DateTimeOffset ExpiresAt);

public readonly record struct CredentialInstant
{
    public CredentialInstant(DateTimeOffset value) => UtcNow = value.ToUniversalTime();

    public DateTimeOffset UtcNow
    {
        get;
    }

    public static CredentialInstant Capture(TimeProvider time) => new(time.GetUtcNow());
}
