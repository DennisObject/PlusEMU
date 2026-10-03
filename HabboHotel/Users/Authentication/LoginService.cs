namespace Plus.HabboHotel.Users.Authentication;

public interface ILoginService
{
    Task<LoginResult> Login(string username, string password, string address);
}

public enum LoginStatus
{
    Success,
    InvalidCredentials,
    Throttled
}

public sealed record LoginResult(LoginStatus Status, string Username = "", IssuedToken SsoTicket = default, IssuedToken AccessToken = default);

/// <summary>
/// Password login for the HTTP API: verifies the password, upgrades legacy rows and hands out a
/// game SSO ticket plus an HTTP access token. Unknown users cost the same hashing work as real
/// ones and get the same answer.
/// </summary>
public class LoginService : ILoginService
{
    private readonly IAccountStore _accounts;
    private readonly IPasswordHasher _hasher;
    private readonly ILoginThrottle _throttle;
    private readonly ISsoTicketStore _ssoTickets;
    private readonly IAccessTokenStore _accessTokens;
    private readonly Lazy<string> _decoyHash;

    public LoginService(IAccountStore accounts, IPasswordHasher hasher, ILoginThrottle throttle, ISsoTicketStore ssoTickets, IAccessTokenStore accessTokens)
    {
        _accounts = accounts;
        _hasher = hasher;
        _throttle = throttle;
        _ssoTickets = ssoTickets;
        _accessTokens = accessTokens;
        _decoyHash = new(() => hasher.Hash(SecureToken.Generate()));
    }

    public async Task<LoginResult> Login(string username, string password, string address)
    {
        if (_throttle.IsBlocked(username, address))
            return new(LoginStatus.Throttled);

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
        var ssoTicket = await _ssoTickets.Issue(account!.Id);
        var accessToken = await _accessTokens.Issue(account.Id);
        return new(LoginStatus.Success, account.Username, ssoTicket, accessToken);
    }
}
