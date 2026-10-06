using System.Collections.Concurrent;
using System.Diagnostics;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// Serializes loading an account into a new session with staff writes to that account. A login holds the
/// gate from its ban check until the session is registered, so a staff write either lands before the
/// account is loaded or finds the registered session. Revocations stop logins whose ticket was already
/// resolved before they reached the gate.
/// </summary>
public interface IAccountSessionGate
{
    /// <summary>Timestamp taken when a login starts, for <see cref="IsRevoked"/>.</summary>
    long Begin();

    Task<IDisposable> EnterAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Holds the stripes of every account, each taken once in ascending stripe order; a failure releases what was taken.</summary>
    Task<IDisposable> EnterManyAsync(IEnumerable<int> userIds, CancellationToken cancellationToken = default);

    IDisposable Enter(int userId);

    /// <summary>Logins of this account that started before now must not complete.</summary>
    void Revoke(int userId);

    bool IsRevoked(int userId, long loginStarted);
}

public sealed class AccountSessionGate : IAccountSessionGate
{
    // Striped so the lock set stays fixed; unrelated accounts rarely share a stripe and only wait briefly.
    private const int Stripes = 64;
    private readonly SemaphoreSlim[] _stripes = Enumerable.Range(0, Stripes).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly ConcurrentDictionary<int, long> _revoked = new();
    private readonly TimeSpan _timeout;

    public AccountSessionGate() : this(TimeSpan.FromSeconds(10)) { }

    internal AccountSessionGate(TimeSpan timeout) => _timeout = timeout;

    public long Begin() => Stopwatch.GetTimestamp();

    public async Task<IDisposable> EnterAsync(int userId, CancellationToken cancellationToken = default)
    {
        var stripe = Stripe(userId);

        if (!await stripe.WaitAsync(_timeout, cancellationToken)) {
            throw new TimeoutException($"Account {userId} is busy.");
        }

        return new Held(stripe);
    }

    public async Task<IDisposable> EnterManyAsync(IEnumerable<int> userIds, CancellationToken cancellationToken = default)
    {
        // Accounts sharing a stripe are one lock, so acquisition is over distinct stripe indexes in ascending order.
        var indexes = userIds.Select(id => (int)((uint)id % Stripes)).Distinct().OrderBy(index => index).ToArray();
        var held = new List<Held>(indexes.Length);

        try {
            foreach (var index in indexes) {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await _stripes[index].WaitAsync(_timeout, cancellationToken)) {
                    throw new TimeoutException("Account is busy.");
                }

                held.Add(new Held(_stripes[index]));
            }
        }
        catch {
            for (var i = held.Count - 1; i >= 0; i--) {
                held[i].Dispose();
            }

            throw;
        }

        return new HeldMany(held);
    }

    public IDisposable Enter(int userId)
    {
        var stripe = Stripe(userId);

        if (!stripe.Wait(_timeout)) {
            throw new TimeoutException($"Account {userId} is busy.");
        }

        return new Held(stripe);
    }

    public void Revoke(int userId) => _revoked[userId] = Stopwatch.GetTimestamp();

    public bool IsRevoked(int userId, long loginStarted) => _revoked.TryGetValue(userId, out var revokedAt) && revokedAt >= loginStarted;

    private SemaphoreSlim Stripe(int userId) => _stripes[(uint)userId % Stripes];

    private sealed class HeldMany(List<Held> held) : IDisposable
    {
        public void Dispose()
        {
            for (var i = held.Count - 1; i >= 0; i--) {
                held[i].Dispose();
            }
        }
    }

    private sealed class Held(SemaphoreSlim stripe) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) {
                stripe.Release();
            }
        }
    }
}
