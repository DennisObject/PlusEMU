using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plus.HabboHotel.Items.DataFormat;

public sealed record HighscoreEntry([property: JsonRequired] int Score, [property: JsonRequired] ImmutableArray<string> Users);

public class HighscoreDataFormat : FurniObjectData
{
    public string State = string.Empty;
    public uint ScoreType;
    public uint ClearType;
    public ImmutableArray<HighscoreEntry> Entries { get; set; } = [];
    public override FurniDataStructure StructureType => FurniDataStructure.HighScore;

    public override string Serialize()
    {
        Validate(State, ScoreType, ClearType, Entries);

        return JsonSerializer.Serialize(new Stored { Version = 1, State = State, ScoreType = ScoreType, ClearType = ClearType, Entries = Entries });
    }

    public override void Store(string data)
    {
        Stored stored;

        try {
            using var document = JsonDocument.Parse(data);
            var properties = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

            if (properties.Length != 5 || properties.Distinct(StringComparer.Ordinal).Count() != 5) {
                throw new ArgumentException("Invalid highscore stored fields.", nameof(data));
            }

            var entries = document.RootElement.GetProperty("Entries");

            foreach (var entry in entries.EnumerateArray()) {
                var entryProperties = entry.EnumerateObject().Select(property => property.Name).ToArray();

                if (entryProperties.Length != 2 || entryProperties.Distinct(StringComparer.Ordinal).Count() != 2) {
                    throw new ArgumentException("Invalid highscore entry fields.", nameof(data));
                }
            }

            stored = document.RootElement.Deserialize<Stored>(new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
                ?? throw new ArgumentException("Missing highscore data.", nameof(data));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) {
            throw new ArgumentException("Invalid highscore stored payload.", nameof(data), exception);
        }

        if (stored.Version != 1) {
            throw new ArgumentException("Unsupported highscore storage version.", nameof(data));
        }

        Validate(stored.State, stored.ScoreType, stored.ClearType, stored.Entries);
        State = stored.State;
        ScoreType = stored.ScoreType;
        ClearType = stored.ClearType;
        Entries = stored.Entries;
        RaiseDataUpdated();
    }

    public ImmutableArray<HighscoreEntry> CaptureEntries()
    {
        Validate(State, ScoreType, ClearType, Entries);

        return Entries;
    }

    public static bool TryDefinition(string classname, out uint scoreType, out uint clearType)
    {
        var parts = classname.Split('*');
        scoreType = parts[0] switch
        {
            "highscore_perteam" => 0,
            "highscore_mostwin" => 1,
            "highscore_classic" => 2,
            "highscore_fastesttime" => 3,
            "highscore_longesttime" => 4,
            _ => uint.MaxValue
        };
        clearType = 0;

        if (scoreType == uint.MaxValue || parts.Length > 2) {
            return false;
        }

        if (parts.Length == 2) {
            if (!uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var variant) || variant is < 1 or > 4) {
                return false;
            }

            clearType = variant - 1;
        }

        return true;
    }

    private static void Validate(string state, uint scoreType, uint clearType, ImmutableArray<HighscoreEntry> entries)
    {
        // Entry counts use the existing Int32 wire envelope; each string uses unsigned-short UTF8.
        if (state == null || Encoding.UTF8.GetByteCount(state) > ushort.MaxValue || scoreType > 4 || clearType > 3
            || entries.IsDefault || entries.Any(entry => entry == null || entry.Users.IsDefault
                || entry.Users.Any(user => user == null || Encoding.UTF8.GetByteCount(user) > ushort.MaxValue))) {
            throw new ArgumentException("Invalid highscore values.");
        }
    }

    private sealed record Stored
    {
        public required int Version { get; init; }
        public required string State { get; init; }
        public required uint ScoreType { get; init; }
        public required uint ClearType { get; init; }
        public required ImmutableArray<HighscoreEntry> Entries { get; init; }
    }
}
