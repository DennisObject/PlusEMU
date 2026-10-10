using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Triggers;

/// <summary>
/// Per-room timer state. The room tick supplies monotonic time; this module creates no timers
/// or threads. Forget a removed box, and clear on room disposal.
/// </summary>
public sealed class WiredTimedTriggers
{
    private readonly Dictionary<uint, long> _nextPeriodicAt = [];
    private readonly Dictionary<uint, long> _firedEpoch = [];

    public bool TryFire(string name, uint boxId, WiredConfiguration config,
        long nowMs, long elapsedMs, long resetEpoch)
    {
        var units = config.IntParams.IsDefaultOrEmpty ? 1 : config.IntParams[0];

        if (units < 1) {
            return false;
        }

        if (name is "wf_trg_at_given_time" or "wf_trg_at_time_long") {
            // Approved legacy contract: at-time-long is one-shot in five-second units.
            var targetMs = units * (name == "wf_trg_at_time_long" ? 5000L : 500L);

            if (elapsedMs < targetMs || _firedEpoch.TryGetValue(boxId, out var fired) && fired == resetEpoch) {
                return false;
            }

            _firedEpoch[boxId] = resetEpoch;

            return true;
        }

        var stepMs = name switch
        {
            "wf_trg_periodically" => 500L,
            "wf_trg_period_short" => 50L,
            "wf_trg_period_long" => 5000L,
            _ => 0L
        };

        if (stepMs == 0) {
            return false;
        }

        var intervalMs = units * stepMs;

        if (!_nextPeriodicAt.TryGetValue(boxId, out var next)) {
            _nextPeriodicAt[boxId] = nowMs + intervalMs;

            return false;
        }

        if (nowMs < next) {
            return false;
        }

        // A late poll fires once, then waits a full period from this firing.
        _nextPeriodicAt[boxId] = nowMs + intervalMs;

        return true;
    }

    public void Forget(uint boxId)
    {
        _nextPeriodicAt.Remove(boxId);
        _firedEpoch.Remove(boxId);
    }

    public void Clear()
    {
        _nextPeriodicAt.Clear();
        _firedEpoch.Clear();
    }
}
