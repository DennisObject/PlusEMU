using Plus.HabboHotel.Moderation;

namespace Plus.HabboHotel.Users.Authentication;

public interface ILoginService
{
    Task<LoginResult> Login(string username, string password, string address, bool remember = false);
}

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    Throttled,
    Banned
}

/// <param name="Session">Set on success.</param>
/// <param name="RetryAfter">How long a throttled caller must wait.</param>
/// <param name="Ban">The account or address ban that refused a correct password.</param>
public sealed record LoginResult(LoginStatus Status, AuthSession? Session = null, TimeSpan RetryAfter = default, LoginBan? Ban = null);

/// <summary>
/// Password login for the HTTP API: verifies the password, upgrades legacy rows and hands out a
/// game SSO ticket plus an HTTP access token. Unknown users cost the same hashing work as real
/// ones and get the same answer; bans are only revealed to callers who know the password.
/// </summary>
public class LoginService : ILoginService
{
    private readonly IAccountStore _accounts;
    private readonly IBoundedPasswordHasher _hasher;
    private readonly ILoginThrottle _throttle;
    private readonly ISessionIssuer _sessions;
    private readonly IBanLookup _bans;

    public LoginService(IAccountStore accounts, IBoundedPasswordHasher hasher, ILoginThrottle throttle, ISessionIssuer sessions, IBanLookup bans)
    {
        _accounts = accounts;
        _hasher = hasher;
        _throttle = throttle;
        _sessions = sessions;
        _bans = bans;
    }

    public async Task<LoginResult> Login(string username, string password, string address, bool remember = false)
    {
        var account = await _accounts.FindByUsername(username);
        var throttleKey = account != null ? LoginThrottle.AccountKey(account.Id) : LoginThrottle.UnknownNameKey(username);
        var blockedFor = _throttle.BlockedFor(throttleKey, address);
        if (blockedFor > TimeSpan.Zero)
            return new(LoginStatus.Throttled, RetryAfter: blockedFor);

        var stored = account?.Password ?? "";
        // Plaintext rows and missing accounts would answer faster than real hashes.
        if (!stored.StartsWith("$argon2id$", StringComparison.Ordinal))
            await _hasher.Verify(password, await DecoyHash());

        var verification = account == null ? PasswordVerificationResult.Failed : await _hasher.Verify(password, stored);
        if (verification == PasswordVerificationResult.Failed)
        {
            _throttle.RecordFailure(throttleKey, address);
            return new(LoginStatus.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            await _accounts.UpgradePassword(account!.Id, stored, await _hasher.Hash(password));

        _throttle.RecordSuccess(throttleKey);
        if (await _bans.Find(account!.Username, address) is { } ban)
        {
            await _sessions.RevokeAll(account.Id);
            return new(LoginStatus.Banned, Ban: ban);
        }

        return new(LoginStatus.Success, await _sessions.Issue(account.Id, account.Username, remember));
    }

    private string? _decoyHash;

    /// <summary>A real hash of a random secret, so failed lookups do the same Argon2id work.</summary>
    private async Task<string> DecoyHash() => _decoyHash ??= await _hasher.Hash(SecureToken.Generate());
}
