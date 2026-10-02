namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public sealed record WiredRoomLogEntry(long Id, int Level, int Source, uint BoxId, string Message, DateTimeOffset Timestamp);
public sealed record WiredRoomLogPage(int Total, int Page, int Amount, IReadOnlyList<WiredRoomLogEntry> Entries);

/// <summary>Room-owned bounded log. The monitor endpoint reads this same buffer.</summary>
public sealed class WiredRoomLog(int capacity = 500)
{
    private readonly Queue<WiredRoomLogEntry> _entries = [];
    private long _nextId;
    private readonly object _gate = new();
    public void Append(int level, uint boxId, string message, DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            _entries.Enqueue(new(++_nextId, level, 0, boxId, message, timestamp));
            while (_entries.Count > Math.Max(1, capacity)) _entries.Dequeue();
        }
    }
    public WiredRoomLogPage Read(int page, int amount, int level = -1, string query = "")
    {
        page = Math.Max(0, page); amount = Math.Clamp(amount, 1, 100);
        lock (_gate)
        {
            var matching = _entries.Reverse().Where(entry => (level < 0 || entry.Level == level)
                && (query.Length == 0 || entry.Message.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
            return new(matching.Length, page, amount, matching.Skip((int)Math.Min(int.MaxValue, page * (long)amount)).Take(amount).ToArray());
        }
    }
    public void Clear() { lock (_gate) _entries.Clear(); }
}
