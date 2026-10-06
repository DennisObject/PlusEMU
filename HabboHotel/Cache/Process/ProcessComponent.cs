using Microsoft.Extensions.Logging;

namespace Plus.HabboHotel.Cache.Process;

public sealed class ProcessComponent : IProcessComponent
{
    private readonly ILogger<ProcessComponent> _logger;
    private readonly TimeProvider _clock;
    private readonly object _timerGate = new();
    private ITimer? _timer;
    private int _disposed;
    private int _timerRunning;

    public ProcessComponent(ILogger<ProcessComponent> logger, TimeProvider clock)
    {
        _logger = logger;
        _clock = clock;
    }

    /// <summary>
    /// How often the timer should execute.
    /// </summary>
    private static readonly int _runtimeInSec = 1200;

    /// <summary>
    /// Initializes the ProcessComponent.
    /// </summary>
    public void Init(Action sweep)
    {
        ArgumentNullException.ThrowIfNull(sweep);

        lock (_timerGate) {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);

            if (_timer != null) {
                throw new InvalidOperationException("The cache process has already been initialized.");
            }

            _timer = _clock.CreateTimer(_ => Run(sweep), null, TimeSpan.FromSeconds(_runtimeInSec), TimeSpan.FromSeconds(_runtimeInSec));
        }
    }

    /// <summary>
    /// Called for each time the timer ticks.
    /// </summary>
    private void Run(Action sweep)
    {
        lock (_timerGate) {
            if (_disposed != 0 || Interlocked.CompareExchange(ref _timerRunning, 1, 0) != 0) {
                return;
            }
        }

        try {
            sweep();
        }
        catch (Exception e) {
            _logger.LogError(e, "Cache cleanup failed");
        }
        finally {
            Volatile.Write(ref _timerRunning, 0);
        }
    }

    /// <summary>
    /// Stops the timer and disposes everything.
    /// </summary>
    public void Dispose()
    {
        lock (_timerGate) {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) {
                return;
            }

            var timer = _timer;
            _timer = null;

            try {
                timer?.Dispose();
            }
            catch (Exception e) {
                _logger.LogError(e, "Failed to dispose the cache cleanup timer");
            }
        }
    }
}
