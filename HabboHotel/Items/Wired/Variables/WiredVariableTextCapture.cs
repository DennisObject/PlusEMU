using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed record WiredVariableCapturer(uint DefinitionId, string Name, IReadOnlyDictionary<int, string>? Labels)
{
    public long? Read(string text)
    {
        if (Labels is { Count: > 0 }) {
            return Labels.Where(pair => string.Equals(pair.Value.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(pair => (long?)pair.Key).FirstOrDefault();
        }

        return long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}

/// <summary>Bounded template matching. Resolve every capture before publishing any context values.</summary>
public static class WiredVariableTextCapture
{
    private static readonly Regex Placeholder = new("#([^#]+)#", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    public static bool TryMatch(string template, string text, int mode, IReadOnlyList<WiredVariableCapturer> capturers,
        out IReadOnlyDictionary<uint, long> values)
    {
        values = new Dictionary<uint, long>();

        if (template.Length > 5000 || text.Length > 1000 || capturers.Count > 100 || mode is < 0 or > 2) {
            return false;
        }

        var byName = new Dictionary<string, WiredVariableCapturer>(StringComparer.OrdinalIgnoreCase);

        foreach (var capturer in capturers) {
            byName[capturer.Name] = capturer; // Ordered stack: last duplicate name wins.
        }

        template = template.Trim();

        if (mode == 2 && template.Length == 0) {
            if (byName.Count != 1 || byName.Values.First().Read(text) is not { } value) {
                return false;
            }

            values = new Dictionary<uint, long> { [byName.Values.First().DefinitionId] = value };

            return true;
        }

        var matches = Placeholder.Matches(template).Cast<Match>().ToArray();

        if (matches.Length is 0 or > 8) {
            return Literal(template, text, mode);
        }

        var names = matches.Select(match => match.Groups[1].Value.Trim()).ToArray();

        if (matches[0].Index == 0 && matches[^1].Index + matches[^1].Length == template.Length
            && matches.Skip(1).Select((match, index) => match.Index == matches[index].Index + matches[index].Length).All(value => value)
            && names.All(byName.ContainsKey)) {
            return Adjacent(text, mode, names.Select(name => byName[name]).ToArray(), out values);
        }

        var literals = new List<string>();
        var cursor = 0;

        for (var index = 0; index < matches.Length; index++) {
            var match = matches[index];

            if (index > 0 && match.Index == cursor) {
                return Literal(template, text, mode);
            }

            literals.Add(template[cursor..match.Index]);
            cursor = match.Index + match.Length;
        }

        literals.Add(template[cursor..]);
        var pattern = new StringBuilder(Regex.Escape(literals[0]));

        for (var index = 0; index < matches.Length - 1; index++) {
            pattern.Append("(?>(.+?)").Append(Regex.Escape(literals[index + 1])).Append(')');
        }

        pattern.Append("(.+)").Append(Regex.Escape(literals[^1]));
        var expression = mode == 0 ? pattern.ToString() : "\\A" + pattern + "\\z";

        try {
            var match = Regex.Match(text, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(25));

            if (!match.Success) {
                return false;
            }

            var captured = new Dictionary<uint, long>();

            for (var index = 0; index < names.Length; index++) {
                if (!byName.TryGetValue(names[index], out var capturer)) {
                    continue;
                }

                if (capturer.Read(match.Groups[index + 1].Value) is not { } value) {
                    return false;
                }

                captured[capturer.DefinitionId] = value;
            }

            values = captured;

            return true;
        }
        catch (RegexMatchTimeoutException) {
            return false;
        }
    }
    private static bool Literal(string template, string text, int mode) => template.Length > 0 && (mode == 0
        ? text.Trim().Contains(template, StringComparison.OrdinalIgnoreCase) : string.Equals(text.Trim(), template, StringComparison.OrdinalIgnoreCase));
    private static bool Adjacent(string text, int mode, WiredVariableCapturer[] capturers, out IReadOnlyDictionary<uint, long> values)
    {
        values = new Dictionary<uint, long>();
        var paths = new Dictionary<int, (int Previous, long Value)>[capturers.Length + 1];
        paths[0] = new() { [0] = (0, 0) };

        for (var index = 0; index < capturers.Length; index++) {
            paths[index + 1] = [];

            foreach (var start in paths[index].Keys.Order()) {
                for (var end = start + 1; end <= Math.Min(text.Length, start + 64); end++) {
                    if (!paths[index + 1].ContainsKey(end) && capturers[index].Read(text[start..end]) is { } value) {
                        paths[index + 1][end] = (start, value);
                    }
                }
            }
        }

        var cursor = mode == 0 ? paths[^1].Keys.DefaultIfEmpty(-1).Max() : text.Length;

        if (!paths[^1].ContainsKey(cursor)) {
            return false;
        }

        var captured = new Dictionary<uint, long>();

        for (var index = capturers.Length - 1; index >= 0; index--) {
            var step = paths[index + 1][cursor];
            captured[capturers[index].DefinitionId] = step.Value;
            cursor = step.Previous;
        }

        values = captured;

        return true;
    }
}
