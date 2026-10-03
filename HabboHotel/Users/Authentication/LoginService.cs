using Plus.HabboHotel.Moderation;

namespace Plus.HabboHotel.Users.Authentication;

public interface ILoginService
{
    Task<LoginResult> Login(string username, string password, string address);
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
public sealed record LoginResult(LoginStatus Status, AuthSession? Session = null, TimeSpan RetryAfter = default, ModerationBan? Ban = null);

/// <summary>
/// Password login for the HTTP API: verifies the password, upgrades legacy rows and hands out a
/// game SSO ticket plus an HTTP access token. Unknown users cost the same hashing work as real
/// ones and get the same answer; bans are only revealed to callers who know the password.
/// </summary>
public class LoginService : ILoginService
{
    private readonly IAccountStore _accounts;
    private readonly IPasswordHasher _hasher;
    private readonly ILoginThrottle _throttle;
    private readonly ISessionIssuer _sessions;
    private readonly IModerationManager _moderation;
    private readonly Lazy<string> _decoyHash;

    public LoginService(IAccountStore accounts, IPasswordHasher hasher, ILoginThrottle throttle, ISessionIssuer sessions, IModerationManager moderation)
    {
        _accounts = accounts;
        _hasher = hasher;
        _throttle = throttle;
        _sessions = sessions;
        _moderation = moderation;
        _decoyHash = new(() => hasher.Hash(SecureToken.Generate()));
    }

    public async Task<LoginResult> Login(string username, string password, string address)
    {
        var blockedFor = _throttle.BlockedFor(username, address);
        if (blockedFor > TimeSpan.Zero)
            return new(LoginStatus.Throttled, RetryAfter: blockedFor);

        var account = await _accounts.FindByUsername(username);
        var stored = account?.Password ?? "";
        // Plaintext rows and missing accounts would answer faster than real hashes.
        if (!stored.StartsWith("$argon2id$", StringComparison.Ordinal))
            _hasher.Verify(password, _decoyHash.Value);

        var verification = account == null ? PasswordVerificationResult.Failed : _hasher.Verify(password, stored);
        if (verification == PasswordVerificationResult.Failed)
        {
            _throttle.RecordFailure(username, address);
            return new(LoginStatus.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            await _accounts.UpgradePassword(account!.Id, stored, _hasher.Hash(password));

        _throttle.RecordSuccess(username);
        if (_moderation.IsBanned(account!.Username, out var ban) || _moderation.IsBanned(address, out ban))
            return new(LoginStatus.Banned, Ban: ban);

        return new(LoginStatus.Success, await _sessions.Issue(account.Id, account.Username));
    }
}
