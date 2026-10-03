using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// Counts failed password logins per typed username and per client address in a fixed
/// window. Usernames are counted whether or not the account exists, so a lockout reveals nothing.
/// </summary>
public class LoginThrottle : ILoginThrottle
{
    private const int SweepThreshold = 10_000;

    private readonly ConcurrentDictionary<string, Window> _failures = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _window;
    private readonly int _accountLimit;
    private readonly int _addressLimit;

    public LoginThrottle(TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _time = time;
        _window = TimeSpan.FromMinutes(options.Value.FailedLoginWindowMinutes);
        _accountLimit = options.Value.MaxFailedLoginsPerAccount;
        _addressLimit = options.Value.MaxFailedLoginsPerAddress;
    }

    public TimeSpan BlockedFor(string username, string address)
    {
        var account = Remaining(AccountKey(username), _accountLimit);
        var byAddress = Remaining(AddressKey(address), _addressLimit);
        return account > byAddress ? account : byAddress;
    }

    public void RecordFailure(string username, string address)
    {
        Sweep();
        Increment(AccountKey(username));
        Increment(AddressKey(address));
    }

    /// <summary>Clears the account counter. The address counter keeps running, so one valid
    /// account cannot be used to reset guessing against others.</summary>
    public void RecordSuccess(string username) => _failures.TryRemove(AccountKey(username), out _);

    private TimeSpan Remaining(string key, int limit) =>
        _failures.TryGetValue(key, out var window) && !Expired(window) && window.Count >= limit ? window.Start + _window - _time.GetUtcNow() : TimeSpan.Zero;

    private void Increment(string key)
    {
        var now = _time.GetUtcNow();
        _failures.AddOrUpdate(key, _ => new(now, 1), (_, window) => Expired(window) ? new(now, 1) : window with { Count = window.Count + 1 });
    }

    private void Sweep()
    {
        if (_failures.Count < SweepThreshold)
            return;
        foreach (var (key, window) in _failures)
        {
            if (Expired(window))
                _failures.TryRemove(key, out _);
        }
    }

    private bool Expired(Window window) => _time.GetUtcNow() - window.Start >= _window;

    // users.username holds at most 125 characters; longer input cannot name an account and is
    // cut so a flood of huge names cannot bloat the table.
    private static string AccountKey(string username)
    {
        var name = username.Trim().ToLowerInvariant();
        return "u:" + (name.Length > 125 ? name[..125] : name);
    }
    private static string AddressKey(string address) => "a:" + address;

    private sealed record Window(DateTimeOffset Start, int Count);
}
