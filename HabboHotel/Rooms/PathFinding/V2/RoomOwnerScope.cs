namespace Plus.HabboHotel.Rooms.PathFinding;

public static class RoomOwnerScope
{
    [ThreadStatic]
    private static Room? _currentOwner;

    public static Room? CurrentOwner => _currentOwner;

    public static bool IsOwner(Room room) => ReferenceEquals(_currentOwner, room);

    // Synchronous room ticks use using/try-finally; ownership never flows to another thread.
    public static IDisposable Enter(Room room)
    {
        var scope = new Scope(_currentOwner);
        _currentOwner = room;
        return scope;
    }

    private sealed class Scope(Room? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _currentOwner = previous;
            _disposed = true;
        }
    }
}
