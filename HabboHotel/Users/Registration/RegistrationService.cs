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
    Taken
}

public sealed record RegistrationResult(RegistrationStatus Status, string Error = "");

public sealed record Availability(bool Available, string Error = "");

public class RegistrationService : IRegistrationService
{
    private const string UsernameTaken = "That Habbo name is already taken.";
    private const string EmailTaken = "This email is already in use.";

    // users.mail has no unique index (older rows share placeholder addresses), so the
    // check-then-insert for email runs one registration at a time.
    private readonly SemaphoreSlim _registrationLock = new(1, 1);

    private readonly IAccountStore _accounts;
    private readonly IPasswordHasher _hasher;
    private readonly IWordFilterManager _wordFilter;
    private readonly RegistrationDefaults _defaults;

    public RegistrationService(IAccountStore accounts, IPasswordHasher hasher, IWordFilterManager wordFilter, IOptions<AuthApiConfiguration> options)
    {
        _accounts = accounts;
        _hasher = hasher;
        _wordFilter = wordFilter;
        _defaults = options.Value.Registration;
    }

    public async Task<RegistrationResult> Register(RegistrationRequest request)
    {
        var error = UsernameError(request.Username) ?? RegistrationValidator.EmailError(request.Email) ?? RegistrationValidator.PasswordError(request.Password, request.Username);
        if (error != null)
            return new(RegistrationStatus.Invalid, error);

        var passwordHash = _hasher.Hash(request.Password);
        var account = new NewAccount(request.Username, passwordHash, request.Email, RegistrationValidator.FigureOrDefault(request.Figure, _defaults.Look),
            RegistrationValidator.Gender(request.Gender), request.Address);

        await _registrationLock.WaitAsync();
        try
        {
            if (await _accounts.UsernameExists(request.Username))
                return new(RegistrationStatus.Taken, UsernameTaken);
            if (await _accounts.EmailExists(request.Email))
                return new(RegistrationStatus.Taken, EmailTaken);
            // The unique username index still decides races with writers outside this process.
            return await _accounts.Create(account) == null ? new(RegistrationStatus.Taken, UsernameTaken) : new(RegistrationStatus.Created);
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    public async Task<Availability> CheckUsername(string username)
    {
        if (UsernameError(username) is { } error)
            return new(false, error);
        return await _accounts.UsernameExists(username) ? new(false, UsernameTaken) : new(true);
    }

    public async Task<Availability> CheckEmail(string email)
    {
        if (RegistrationValidator.EmailError(email) is { } error)
            return new(false, error);
        return await _accounts.EmailExists(email) ? new(false, EmailTaken) : new(true);
    }

    private string? UsernameError(string username) => RegistrationValidator.UsernameError(username, _defaults.ReservedNames, _wordFilter.IsFiltered);
}
