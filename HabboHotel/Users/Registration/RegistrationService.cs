using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users.Authentication;

namespace Plus.HabboHotel.Users.Registration;

public interface IRegistrationService
{
    Task<RegistrationResult> Register(RegistrationRequest request);
    Task<Availability> CheckUsername(string username);
    Task<Availability> CheckEmail(string email);
}

public sealed record RegistrationRequest(string Username, string Password, string Email, string? Figure, string? Gender, string Address);

public enum RegistrationStatus
{
    Created,
    Invalid,
    UsernameTaken,
    EmailTaken
}

/// <param name="Session">Set when created: the new user is logged in straight away (null only if
/// the account's credentials were revoked within that instant).</param>
public sealed record RegistrationResult(RegistrationStatus Status, string Error = "", AuthSession? Session = null);

/// <param name="Reason">Why the value is unavailable: Invalid, UsernameTaken or EmailTaken.</param>
public sealed record Availability(bool Available, RegistrationStatus Reason = RegistrationStatus.Created, string Error = "");

public class RegistrationService : IRegistrationService
{
    private const string UsernameTaken = "That Habbo name is already taken.";
    private const string EmailTaken = "This email is already in use.";

    // users.mail has no unique index (older rows share placeholder addresses), so the
    // check-then-insert for email runs one registration at a time.
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    private readonly IAccountStore _accounts;
    private readonly IBoundedPasswordHasher _hasher;
    private readonly ISessionIssuer _sessions;
    private readonly IWordFilterManager _wordFilter;
    private readonly RegistrationDefaults _defaults;

    public RegistrationService(IAccountStore accounts, IBoundedPasswordHasher hasher, ISessionIssuer sessions, IWordFilterManager wordFilter, IOptions<AuthApiConfiguration> options)
    {
        _accounts = accounts;
        _hasher = hasher;
        _sessions = sessions;
        _wordFilter = wordFilter;
        _defaults = options.Value.Registration;
    }

    public async Task<RegistrationResult> Register(RegistrationRequest request)
    {
        var error = UsernameError(request.Username) ?? RegistrationValidator.EmailError(request.Email) ?? RegistrationValidator.PasswordError(request.Password, request.Username);
        if (error != null)
            return new(RegistrationStatus.Invalid, error);

        var passwordHash = await _hasher.Hash(request.Password);
        var account = new NewAccount(request.Username, passwordHash, request.Email, RegistrationValidator.FigureOrDefault(request.Figure, _defaults.Look),
            RegistrationValidator.Gender(request.Gender), request.Address);

        int? userId;
        await _registrationLock.WaitAsync();
        try
        {
            if (await _accounts.UsernameExists(request.Username))
                return new(RegistrationStatus.UsernameTaken, UsernameTaken);
            if (await _accounts.EmailExists(request.Email))
                return new(RegistrationStatus.EmailTaken, EmailTaken);
            // The unique username index still decides races with writers outside this process.
            userId = await _accounts.Create(account);
        }
        finally
        {
            _registrationLock.Release();
        }

        if (userId is not { } id)
            return new(RegistrationStatus.UsernameTaken, UsernameTaken);
        // A new account starts at generation 0, so a revoke any time after the insert voids this session.
        var session = await _sessions.Issue(id, request.Username, AccountStore.NewAccountGeneration);
        return new(RegistrationStatus.Created, Session: session);
    }

    public async Task<Availability> CheckUsername(string username)
    {
        if (UsernameError(username) is { } error)
            return new(false, RegistrationStatus.Invalid, error);
        return await _accounts.UsernameExists(username) ? new(false, RegistrationStatus.UsernameTaken, UsernameTaken) : new(true);
    }

    public async Task<Availability> CheckEmail(string email)
    {
        if (RegistrationValidator.EmailError(email) is { } error)
            return new(false, RegistrationStatus.Invalid, error);
        return await _accounts.EmailExists(email) ? new(false, RegistrationStatus.EmailTaken, EmailTaken) : new(true);
    }

    private string? UsernameError(string username) => RegistrationValidator.UsernameError(username, _defaults.ReservedNames, _wordFilter.IsFiltered);
}
