using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// Argon2id with the OWASP minimum parameters, stored as a PHC string:
/// $argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt; (unpadded base64).
/// </summary>
public class Argon2idPasswordHasher : IPasswordHasher
{
    private const string Prefix = "$argon2id$";
    private const int Version = 19;
    private const int MemoryKiB = 19456;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // Bounds for values read back from the database, so a corrupt row cannot stall the server.
    private const int MaxMemoryKiB = 1024 * 1024;
    private const int MaxIterations = 16;
    private const int MaxParallelism = 16;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, MemoryKiB, Iterations, Parallelism, HashBytes);

        return $"{Prefix}v={Version}$m={MemoryKiB},t={Iterations},p={Parallelism}${ToBase64(salt)}${ToBase64(hash)}";
    }

    public PasswordVerificationResult Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(stored))
        {
            return PasswordVerificationResult.Failed;
        }

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return VerifyLegacyPlaintext(password, stored);
        }

        if (!TryParse(stored, out var parameters))
        {
            return PasswordVerificationResult.Failed;
        }

        var actual = Derive(password, parameters.Salt, parameters.MemoryKiB, parameters.Iterations, parameters.Parallelism, parameters.Hash.Length);

        if (!CryptographicOperations.FixedTimeEquals(actual, parameters.Hash))
        {
            return PasswordVerificationResult.Failed;
        }

        var weaker = parameters.MemoryKiB < MemoryKiB || parameters.Iterations < Iterations || parameters.Hash.Length < HashBytes;

        return weaker ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Success;
    }

    private static PasswordVerificationResult VerifyLegacyPlaintext(string password, string stored)
    {
        // Comparing digests keeps the comparison constant-time and hides the stored length.
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(stored));
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));

        return CryptographicOperations.FixedTimeEquals(actual, expected)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Failed;
    }

    private static byte[] Derive(string password, byte[] salt, int memoryKiB, int iterations, int parallelism, int length)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };

        return argon.GetBytes(length);
    }

    private static bool TryParse(string stored, out PhcParameters parameters)
    {
        parameters = default;
        var parts = stored.Split('$');

        if (parts.Length != 6 || parts[2] != $"v={Version}")
        {
            return false;
        }

        var settings = parts[3].Split(',');

        if (settings.Length != 3
            || !TryReadSetting(settings[0], "m=", MaxMemoryKiB, out var memory)
            || !TryReadSetting(settings[1], "t=", MaxIterations, out var iterations)
            || !TryReadSetting(settings[2], "p=", MaxParallelism, out var parallelism)
            || memory < 8 * parallelism)
        {
            return false;
        }

        if (!TryFromBase64(parts[4], out var salt) || salt.Length < 8
            || !TryFromBase64(parts[5], out var hash) || hash.Length < 16)
        {
            return false;
        }

        parameters = new(memory, iterations, parallelism, salt, hash);

        return true;
    }

    private static bool TryReadSetting(string setting, string name, int max, out int value)
    {
        value = 0;

        return setting.StartsWith(name, StringComparison.Ordinal)
            && int.TryParse(setting.AsSpan(name.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value)
            && value >= 1 && value <= max;
    }

    private static string ToBase64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        bytes = [];

        if (value.Length == 0 || value.Length % 4 == 1)
        {
            return false;
        }

        var padded = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        var buffer = new byte[padded.Length];

        if (!Convert.TryFromBase64String(padded, buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written];

        return true;
    }

    private readonly record struct PhcParameters(int MemoryKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}
