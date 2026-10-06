namespace Plus.HabboHotel.Rooms.PathFinding;

public enum RouteState : byte
{
    Normal, Suspect, Truncated
}

// One retained edge: XY, its original purpose and advisory surface metadata (never trusted as identity).
public readonly record struct RetainedStep(int X, int Y, StepPurpose Purpose, uint SupportItem, double Z);

// Blocked-route lifecycle of one command's route, shared by the legacy and v2 engines.
public sealed class RouteStateMachine
{
    private readonly List<RetainedStep> _retained = new();
    private int _routeIndex;

    public RouteState State
    {
        get; private set;
    }
    public long CommandSequence
    {
        get; private set;
    }
    public IReadOnlyList<RetainedStep> Retained => _retained;

    public void Begin(long commandSequence)
    {
        State = RouteState.Normal;
        CommandSequence = commandSequence;
        _retained.Clear();
        _routeIndex = 0;
    }

    public void Reset() => Begin(CommandSequence);

    // Returns true only for the failure that must request the single fallback search.
    public bool Suspect(IReadOnlyList<RetainedStep> remaining, int routeIndex)
    {
        if (State != RouteState.Normal)
        {
            return false;
        }

        State = RouteState.Suspect;
        _retained.Clear();
        _retained.AddRange(remaining);
        _routeIndex = routeIndex;

        return true;
    }

    public void Consume(int routeIndex)
    {
        var consumed = Math.Clamp(routeIndex - _routeIndex, 0, _retained.Count);
        _retained.RemoveRange(0, consumed);
        _routeIndex = routeIndex;
    }

    // The walk route was rebuilt from Retained[0].
    public void Rebind() => _routeIndex = 0;

    public void Found()
    {
        if (State != RouteState.Suspect)
        {
            return;
        }

        State = RouteState.Normal;
        _retained.Clear();
        _routeIndex = 0;
    }

    public void Truncate(int prefixLength)
    {
        if (State != RouteState.Suspect)
        {
            return;
        }

        State = RouteState.Truncated;
        Cut(prefixLength);
    }

    public void Shorten(int prefixLength)
    {
        if (State == RouteState.Truncated)
        {
            Cut(prefixLength);
        }
    }

    private void Cut(int length)
    {
        if (length < _retained.Count)
        {
            _retained.RemoveRange(length, _retained.Count - length);
        }
    }
}
