namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>
/// Where a room log line came from, numbered as the client's log source filter and monitor rows
/// number them. Plus only produces these four: the engine has no heavy, overload or kill state.
/// </summary>
public enum WiredLogSource
{
    ExecutionCap = 0, DelayedEventsCap = 1, RecursionTimeout = 5, WiredLog = 8
}

public sealed record WiredRoomLogEntry(long Id, int Level, WiredLogSource Source, uint BoxId, string Label, string Reason, DateTimeOffset Timestamp)
{
    public static string TypeName(WiredLogSource source) => source switch
    {
        WiredLogSource.ExecutionCap => "EXECUTION_CAP",
        WiredLogSource.DelayedEventsCap => "DELAYED_EVENTS_CAP",
        WiredLogSource.RecursionTimeout => "RECURSION_TIMEOUT",
        _ => "WIRED_LOG"
    };
    public static string LevelName(int level) => level switch { 0 => "DEBUG", 1 => "INFO", 2 => "WARNING", _ => "ERROR" };
    // A "write to logs" line reads as the box wrote it; an engine note names its limit.
    public string Message => Source == WiredLogSource.WiredLog ? Reason : $"{TypeName(Source)}: {Reason}";
}

public sealed record WiredRoomLogPage(int Total, int Page, int Amount, IReadOnlyList<WiredRoomLogEntry> Entries);
/// <summary>Every occurrence of one source since the log was last cleared, including lines the buffer has dropped.</summary>
public sealed record WiredRoomLogTally(WiredLogSource Source, int Count, WiredRoomLogEntry? Latest);
public sealed record WiredRoomLogSummary(IReadOnlyList<WiredRoomLogTally> Tallies, IReadOnlyList<WiredRoomLogEntry> Recent);

/// <summary>Room-owned bounded log. The log page and the monitor read this same buffer.</summary>
public sealed class WiredRoomLog(int capacity = 500)
{
    public const int ErrorLevel = 3;
    private static readonly WiredLogSource[] Sources = Enum.GetValues<WiredLogSource>();
    private readonly Queue<WiredRoomLogEntry> _entries = [];
    private readonly Dictionary<WiredLogSource, (int Count, WiredRoomLogEntry Latest)> _tallies = [];
    private long _nextId;
    private readonly object _gate = new();

    public void Append(int level, uint boxId, string message, DateTimeOffset timestamp) =>
        Append(level, WiredLogSource.WiredLog, boxId, "", message, timestamp);

    public void Append(int level, WiredLogSource source, uint boxId, string label, string reason, DateTimeOffset timestamp)
    {
        lock (_gate) {
            var entry = new WiredRoomLogEntry(++_nextId, level, source, boxId, label, reason, timestamp);
            _entries.Enqueue(entry);

            while (_entries.Count > Math.Max(1, capacity)) {
                _entries.Dequeue();
            }

            _tallies[source] = (_tallies.GetValueOrDefault(source).Count + 1, entry);
        }
    }

    public WiredRoomLogPage Read(int page, int amount, int level = -1, string query = "", int source = -1)
    {
        page = Math.Max(0, page);
        amount = Math.Clamp(amount, 1, 100);

        lock (_gate) {
            var matching = _entries.Reverse().Where(entry => (level < 0 || entry.Level == level)
                && (source < 0 || (int)entry.Source == source)
                && (query.Length == 0 || entry.Message.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
            // A page past the end, after a clear or a narrower filter, reads as the last page.
            page = Math.Min(page, Math.Max(0, (matching.Length - 1) / amount));

            return new(matching.Length, page, amount, matching.Skip(page * amount).Take(amount).ToArray());
        }
    }

    public WiredRoomLogSummary Summarize(int recent)
    {
        lock (_gate) {
            return new(Sources.Select(source => _tallies.TryGetValue(source, out var tally)
                    ? new WiredRoomLogTally(source, tally.Count, tally.Latest) : new(source, 0, null)).ToArray(),
                _entries.Reverse().Take(Math.Max(0, recent)).ToArray());
        }
    }

    public void Clear()
    {
        lock (_gate) {
            _entries.Clear();
            _tallies.Clear();
        }
    }
}
