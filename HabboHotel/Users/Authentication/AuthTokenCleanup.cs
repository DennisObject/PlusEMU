using Microsoft.Extensions.Logging;
using Plus.Core;

namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// Deletes access and remember tokens a day after they expire, then login sessions nothing refers
/// to any more, every ten minutes in small batches. An expired token is refused either way, so
/// nothing is needed for reuse detection.
/// </summary>
public class AuthTokenCleanup : IStartable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private const int RetentionSeconds = 24 * 60 * 60;
    private const int MaxBatchesPerRun = 100;

    private readonly IRememberTokenStore _rememberTokens;
    private readonly IAccessTokenStore _accessTokens;
    private readonly ICredentialGenerations _sessions;
    private readonly TimeProvider _time;
    private readonly ILogger<AuthTokenCleanup> _logger;

    public AuthTokenCleanup(IRememberTokenStore rememberTokens, IAccessTokenStore accessTokens, ICredentialGenerations sessions, TimeProvider time,
        ILogger<AuthTokenCleanup> logger)
    {
        _rememberTokens = rememberTokens;
        _accessTokens = accessTokens;
        _sessions = sessions;
        _time = time;
        _logger = logger;
    }

    internal int BatchSize { get; init; } = 1000;

    public Task Start()
    {
        _ = Task.Run(RunForever);
        return Task.CompletedTask;
    }

    /// <summary>One cleanup pass; returns the number of rows deleted.</summary>
    public async Task<int> PruneExpired()
    {
        var cutoff = _time.GetUtcNow().ToUnixTimeSeconds() - RetentionSeconds;
        return await PruneInBatches(batch => _rememberTokens.Prune(cutoff, batch))
            + await PruneInBatches(batch => _accessTokens.Prune(cutoff, batch))
            + await PruneInBatches(batch => _sessions.PruneSessions(cutoff, batch));
    }

    private async Task<int> PruneInBatches(Func<int, Task<int>> prune)
    {
        var total = 0;
        for (var i = 0; i < MaxBatchesPerRun; i++)
        {
            var deleted = await prune(BatchSize);
            total += deleted;
            if (deleted < BatchSize)
                break;
        }
        return total;
    }

    private async Task RunForever()
    {
        using var timer = new PeriodicTimer(Interval, _time);
        do
        {
            try
            {
                await PruneExpired();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Expired login token cleanup failed; retrying in {Interval}.", Interval);
            }
        } while (await timer.WaitForNextTickAsync());
    }
}
