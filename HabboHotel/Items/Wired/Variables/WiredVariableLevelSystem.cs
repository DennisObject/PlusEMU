using System.Globalization;
using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Variables.Fx;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Compiled level thresholds shared by derived reads and level FX; bounded work occurs at configuration time.</summary>
public sealed class WiredVariableLevelSystem
{
    private readonly long[] _thresholds;
    public int SubvariableMask { get; }
    private WiredVariableLevelSystem(long[] thresholds, int mask)
    {
        _thresholds = thresholds;
        SubvariableMask = mask;
    }
    public static bool TryParse(string text, out WiredVariableLevelSystem? result)
    {
        result = null;

        if (text.Length > 8192) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            int Number(string name, int fallback) => root.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
            int NonNegative(string name, int fallback) => Math.Max(0, Number(name, fallback));
            var mode = Number("mode", 1);

            if (mode is < 1 or > 3) {
                return false;
            }

            var maxLevel = Math.Clamp(Number("maxLevel", 10), 1, 10000);
            var thresholds = new long[maxLevel];

            if (mode == 1) {
                var step = NonNegative("stepSize", 100);

                for (var i = 1; i < thresholds.Length; i++) {
                    thresholds[i] = Clamp((long)i * step);
                }
            }
            else if (mode == 2) {
                long increment = NonNegative("firstLevelXp", 100);
                var factor = NonNegative("increaseFactor", 100);
                long threshold = 0;

                for (var i = 1; i < thresholds.Length; i++) {
                    threshold = Clamp((decimal)threshold + increment);
                    thresholds[i] = Clamp(threshold);
                    increment = Clamp(decimal.Floor(increment * (100m + factor) / 100m + 0.5m));
                }
            }
            else {
                var manual = root.TryGetProperty("interpolationText", out var value) ? value.GetString() ?? "" : "";

                if (manual.Length > 4096) {
                    return false;
                }

                var anchors = new SortedDictionary<int, long>();

                foreach (var line in manual.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) {
                    var separator = line.IndexOf('=');

                    if (separator < 0) {
                        separator = line.IndexOf(',');
                    }

                    if (separator <= 0 || !int.TryParse(line[..separator].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
                        || !long.TryParse(line[(separator + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var xp)
                        || level is < 1 or > 10000 || xp < 0) {
                        return false;
                    }

                    anchors[level] = xp;
                }

                anchors.TryAdd(1, 0);
                thresholds = new long[anchors.Keys.Last()];
                var previous = anchors.First();
                thresholds[0] = previous.Value;

                foreach (var next in anchors.Skip(1)) {
                    for (var level = previous.Key + 1; level <= next.Key; level++) {
                        var ratio = (decimal)(level - previous.Key) / (next.Key - previous.Key);
                        thresholds[level - 1] = Clamp(decimal.Floor(previous.Value + ((decimal)next.Value - previous.Value) * ratio + 0.5m));
                    }

                    previous = next;
                }
            }

            var mask = 3;

            if (root.TryGetProperty("subvariables", out var selected)) {
                mask = 0;

                foreach (var value in selected.EnumerateArray()) {
                    var index = value.GetInt32();

                    if (index is < 0 or > 7) {
                        return false;
                    }

                    mask |= 1 << index;
                }
            }

            result = new(thresholds, mask);

            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException) {
            return false;
        }
    }
    public WiredVariableLevel Level(long rawXp)
    {
        var xp = Math.Max(0, rawXp);
        var current = 0;
        long start = 0;
        long next = 0;

        // Manual thresholds need not be monotonic; Polaris stops at the first unreached entry.
        for (var i = 0; i < _thresholds.Length; i++) {
            if (xp < _thresholds[i]) {
                next = _thresholds[i];
                break;
            }

            current = i;
            start = _thresholds[i];
            next = _thresholds[Math.Min(i + 1, _thresholds.Length - 1)];
        }

        var maxed = current == _thresholds.Length - 1;

        return new(current + 1, _thresholds.Length, start, maxed ? start : next, maxed);
    }
    public long Read(long rawXp, int subvariable)
    {
        var xp = Math.Max(0, rawXp);
        var level = Level(xp);
        var progress = Math.Max(0L, xp - level.Start);
        var delta = Math.Max(0L, level.Next - level.Start);

        return subvariable switch
        {
            0 => level.Level,
            1 => xp,
            2 => progress,
            3 => level.IsMaxed || delta == 0 ? 100 : (long)Math.Clamp((decimal)progress * 100 / delta, 0m, 100m),
            4 => level.Next,
            5 => Math.Max(0L, level.Next - xp),
            6 => level.IsMaxed ? 1 : 0,
            7 => level.MaxLevel,
            _ => throw new ArgumentOutOfRangeException(nameof(subvariable))
        };
    }
    private static long Clamp(decimal value) => (long)Math.Clamp(value, 0m, long.MaxValue);
}
