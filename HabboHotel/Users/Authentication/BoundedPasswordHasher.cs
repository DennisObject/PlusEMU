using Microsoft.Extensions.Options;
using Plus.Communication.Http;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// The password hasher for request handlers: at most AuthApi:MaxConcurrentPasswordChecks hashes
/// run at once (each Argon2id check takes about 19 MiB) and the rest wait asynchronously. Only
/// the hash itself holds a slot, whatever route or request body led to it.
/// </summary>
public interface IBoundedPasswordHasher
{
    Task<string> Hash(string password);
    Task<PasswordVerificationResult> Verify(string password, string stored);
}

public class BoundedPasswordHasher : IBoundedPasswordHasher
{
    private readonly IPasswordHasher _inner;
    private readonly SemaphoreSlim _slots;

    public BoundedPasswordHasher(IPasswordHasher inner, IOptions<AuthApiConfiguration> options)
    {
        _inner = inner;
        _slots = new(options.Value.MaxConcurrentPasswordChecks, options.Value.MaxConcurrentPasswordChecks);
    }

    public Task<string> Hash(string password) => Run(() => _inner.Hash(password));

    public Task<PasswordVerificationResult> Verify(string password, string stored) => Run(() => _inner.Verify(password, stored));

    private async Task<T> Run<T>(Func<T> hash)
    {
        await _slots.WaitAsync();
        try
        {
            return hash();
        }
        finally
        {
            _slots.Release();
        }
    }
}
