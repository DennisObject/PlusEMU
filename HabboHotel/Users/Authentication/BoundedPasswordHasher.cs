using Microsoft.Extensions.Options;
using Plus.Communication.Http;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// The password hasher for request handlers. At most AuthApi:MaxConcurrentPasswordChecks hashes
/// run at once (each Argon2id check takes about 19 MiB), at most AuthApi:MaxQueuedPasswordChecks
/// wait, and anything beyond that is refused with <see cref="PasswordCheckQueueFullException"/>.
/// Only the hash itself holds a slot, whatever route or request body led to it.
/// </summary>
public interface IBoundedPasswordHasher
{
    Task<string> Hash(string password, CancellationToken cancellationToken = default);
    Task<PasswordVerificationResult> Verify(string password, string stored, CancellationToken cancellationToken = default);
}

public class PasswordCheckQueueFullException() : Exception("Too many password checks are waiting.");

public class BoundedPasswordHasher : IBoundedPasswordHasher
{
    private readonly IPasswordHasher _inner;
    private readonly SemaphoreSlim _slots;
    private readonly int _maxQueued;
    private int _queued;

    public BoundedPasswordHasher(IPasswordHasher inner, IOptions<AuthApiConfiguration> options)
    {
        _inner = inner;
        _slots = new(options.Value.MaxConcurrentPasswordChecks, options.Value.MaxConcurrentPasswordChecks);
        _maxQueued = options.Value.MaxQueuedPasswordChecks;
    }

    public Task<string> Hash(string password, CancellationToken cancellationToken = default) =>
        Run(() => _inner.Hash(password), cancellationToken);

    public Task<PasswordVerificationResult> Verify(string password, string stored, CancellationToken cancellationToken = default) =>
        Run(() => _inner.Verify(password, stored), cancellationToken);

    private async Task<T> Run<T>(Func<T> hash, CancellationToken cancellationToken)
    {
        if (!_slots.Wait(0))
        {
            if (Interlocked.Increment(ref _queued) > _maxQueued)
            {
                Interlocked.Decrement(ref _queued);
                throw new PasswordCheckQueueFullException();
            }

            try
            {
                await _slots.WaitAsync(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _queued);
            }
        }

        try
        {
            // A caller that went away while queued gives its slot back without hashing.
            cancellationToken.ThrowIfCancellationRequested();

            return hash();
        }
        finally
        {
            _slots.Release();
        }
    }
}
