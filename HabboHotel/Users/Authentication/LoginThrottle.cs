using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// Counts failed password logins per account and per client address in a fixed window.
/// Existing accounts are counted by user id, so every spelling the database resolves to the
/// same account shares one counter. Unknown names are counted too (folded for case and
/// accents), so a lockout does not reveal whether an account exists.
/// </summary>
public class LoginThrottle : ILoginThrottle
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, Window> _failures = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _window;
    private readonly int _accountLimit;
    private readonly int _addressLimit;
    private readonly int _capacity;
    // ConcurrentDictionary.Count takes every internal lock, so the size is tracked here.
    private int _tracked;
    private DateTimeOffset _nextSweep;

    public LoginThrottle(TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _time = time;
        _window = TimeSpan.FromMinutes(options.Value.FailedLoginWindowMinutes);
        _accountLimit = options.Value.MaxFailedLoginsPerAccount;
        _addressLimit = options.Value.MaxFailedLoginsPerAddress;
        _capacity = options.Value.MaxTrackedLoginFailures;
        _nextSweep = time.GetUtcNow() + SweepInterval;
    }

    public static string AccountKey(int userId) => "id:" + userId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Key for a name that matched no account: trimmed, lower-cased, accents removed and
    /// cut to the 125 characters users.username can hold.</summary>
    public static string UnknownNameKey(string username)
    {
        var folded = new StringBuilder();

        foreach (var c in username.Trim().Normalize(NormalizationForm.FormD)) {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) {
                folded.Append(char.ToLowerInvariant(c));
            }

            if (folded.Length == 125) {
                break;
            }
        }

        return "name:" + folded;
    }

    public TimeSpan BlockedFor(string accountKey, string address)
    {
        MaybeSweep();
        var account = Remaining(accountKey, _accountLimit);
        var byAddress = Remaining(AddressKey(address), _addressLimit);

        return account > byAddress ? account : byAddress;
    }

    public void RecordFailure(string accountKey, string address)
    {
        MaybeSweep();
        Increment(accountKey);
        Increment(AddressKey(address));
    }

    /// <summary>Clears the account counter. The address counter keeps running, so one valid
    /// account cannot be used to reset guessing against others.</summary>
    public void RecordSuccess(string accountKey)
    {
        if (_failures.TryRemove(accountKey, out _)) {
            Interlocked.Decrement(ref _tracked);
        }
    }

    private TimeSpan Remaining(string key, int limit)
    {
        if (!_failures.TryGetValue(key, out var window) || Expired(window)) {
            // A full table cannot record new failures, so untracked callers are treated as locked.
            return Full ? _window : TimeSpan.Zero;
        }

        return window.Count >= limit ? window.Start + _window - _time.GetUtcNow() : TimeSpan.Zero;
    }

    private void Increment(string key)
    {
        var now = _time.GetUtcNow();

        while (true) {
            if (_failures.TryGetValue(key, out var current)) {
                var next = Expired(current) ? new Window(now, 1) : current with { Count = current.Count + 1 };

                if (_failures.TryUpdate(key, next, current)) {
                    return;
                }
            }
            else {
                if (Full) {
                    return;
                }

                if (_failures.TryAdd(key, new(now, 1))) {
                    Interlocked.Increment(ref _tracked);

                    return;
                }
            }
        }
    }

    private bool Full => Volatile.Read(ref _tracked) >= _capacity;

    /// <summary>Drops expired windows at most every 30 seconds, so recording stays O(1).</summary>
    private void MaybeSweep()
    {
        var now = _time.GetUtcNow();

        if (now < _nextSweep) {
            return;
        }

        _nextSweep = now + SweepInterval;

        foreach (var (key, window) in _failures) {
            if (Expired(window) && _failures.TryRemove(new KeyValuePair<string, Window>(key, window))) {
                Interlocked.Decrement(ref _tracked);
            }
        }
    }

    private bool Expired(Window window) => _time.GetUtcNow() - window.Start >= _window;

    private static string AddressKey(string address) => "a:" + address;

    private sealed record Window(DateTimeOffset Start, int Count);
}
