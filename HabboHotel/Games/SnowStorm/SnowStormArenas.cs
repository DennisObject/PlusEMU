using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Games.SnowStorm.Simulation;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>An official arena: AIR field type (8 Arctic Island, 9 Dragon Top, 11 Fight Night), level and team spawn tiles.</summary>
public sealed record SnowStormArenaDefinition(
    int FieldType,
    string Name,
    SnowStormLevelData Level,
    IReadOnlyDictionary<int, IReadOnlyList<(int X, int Y)>> Spawns);

/// <summary>
/// The level one game is played on and the furni MapStuffData (by fuse id) of its decoration, e.g. the official backdrop.
/// </summary>
public sealed record SnowStormArenaLevel(SnowStormLevelData Level, ImmutableDictionary<int, ImmutableArray<KeyValuePair<string, string>>> MapStuff);

public interface ISnowStormArenas
{
    IReadOnlyList<SnowStormArenaDefinition> All { get; }

    bool TryGet(int fieldType, [NotNullWhen(true)] out SnowStormArenaDefinition? arena);
}

/// <summary>
/// Loads the arenas from <c>snowstorm/arena_*.json</c> next to the emulator (copied from Resources/SnowStorm). Each item
/// line is <c>classname x y direction [altitude]</c>; footprint, height and walkability come from the furni table below.
/// </summary>
public sealed class SnowStormArenas : ISnowStormArenas
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    // Furnidata footprint and visual height (z * 1600 world units) of the official SnowStorm furni; none can be stood on.
    private static readonly IReadOnlyDictionary<string, (int XDimension, int YDimension, int Height)> Furni = new Dictionary<string, (int, int, int)>
    {
        ["snst_block1"] = (1, 1, 1440),
        ["snst_iceblock"] = (1, 1, 1760),
        ["snst_tree1"] = (1, 1, 3200),
        ["snst_tree1_d"] = (1, 1, 3200),
        ["snst_fence"] = (1, 2, 960),
        ["snst_ballpile"] = (1, 1, 320),
        ["s_snowball_machine"] = (1, 1, 1600),
        ["ads_igorraygun"] = (1, 2, 1600),
        ["xm09_man_a"] = (1, 1, 1280),
        ["xm09_man_b"] = (1, 1, 1200),
        ["xm09_man_c"] = (1, 1, 800)
    };

    // Polaris addOfficialBackground: an ads_background at (0, y) facing 45°, lifted by offsetZ.
    private static readonly IReadOnlyDictionary<int, (int Y, int OffsetZ)> Backgrounds = new Dictionary<int, (int, int)>
    {
        [8] = (19, 10000),
        [9] = (22, 9920),
        [11] = (22, 9950)
    };

    private readonly Lazy<IReadOnlyList<SnowStormArenaDefinition>> _arenas;

    public SnowStormArenas(ILogger<SnowStormArenas> logger) : this(Path.Join(AppContext.BaseDirectory, "snowstorm"), logger) { }

    internal SnowStormArenas(string directory, ILogger<SnowStormArenas> logger) =>
        _arenas = new(() => Load(directory, logger));

    public IReadOnlyList<SnowStormArenaDefinition> All => _arenas.Value;

    public bool TryGet(int fieldType, [NotNullWhen(true)] out SnowStormArenaDefinition? arena)
    {
        arena = All.FirstOrDefault(candidate => candidate.FieldType == fieldType);

        return arena != null;
    }

    /// <summary>
    /// The arena's level plus its official backdrop when a URL is set. The backdrop can be stood on and has no height,
    /// so like any fuse object it only registers on its tile and never changes walkability or snowball collisions.
    /// </summary>
    public static SnowStormArenaLevel ForGame(SnowStormArenaDefinition arena, string? backgroundUrl)
    {
        var level = arena.Level;

        if (string.IsNullOrWhiteSpace(backgroundUrl) || !Backgrounds.TryGetValue(arena.FieldType, out var background)) {
            return new(level, ImmutableDictionary<int, ImmutableArray<KeyValuePair<string, string>>>.Empty);
        }

        var id = level.FuseObjects.Count + 1;
        var backdrop = new SnowStormFuseObject("ads_background", id, 0, background.Y, 1, 1, 0, 1, 0, true, "0");
        ImmutableArray<KeyValuePair<string, string>> stuff =
        [
            new("state", "0"), new("imageUrl", backgroundUrl), new("offsetX", "0"), new("offsetY", "0"),
            new("offsetZ", background.OffsetZ.ToString(System.Globalization.CultureInfo.InvariantCulture))
        ];

        return new(level with { FuseObjects = [.. level.FuseObjects, backdrop] }, ImmutableDictionary<int, ImmutableArray<KeyValuePair<string, string>>>.Empty.Add(id, stuff));
    }

    internal static SnowStormArenaDefinition Parse(string json)
    {
        var file = JsonSerializer.Deserialize<ArenaFile>(json, Options) ?? throw new InvalidDataException("Empty arena file.");
        var rows = file.Heightmap ?? [];
        var width = rows.Length == 0 ? 0 : rows[0].Length;

        if (width == 0 || rows.Any(row => row.Length != width)) {
            throw new InvalidDataException($"Arena {file.FieldType} needs a rectangular heightmap.");
        }

        var objects = ImmutableArray.CreateBuilder<SnowStormFuseObject>();

        foreach (var line in file.Items ?? []) {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length is < 4 or > 5 || !Furni.TryGetValue(tokens[0], out var furni) ||
                !int.TryParse(tokens[1], out var x) || !int.TryParse(tokens[2], out var y) || !int.TryParse(tokens[3], out var direction) ||
                x < 0 || y < 0 || x >= width || y >= rows.Length || direction is < 0 or > 7) {
                throw new InvalidDataException($"Arena {file.FieldType} has an invalid item '{line}'.");
            }

            var altitude = tokens.Length == 5 && int.TryParse(tokens[4], out var value) ? value : 0;
            objects.Add(new SnowStormFuseObject(tokens[0], objects.Count + 1, x, y, furni.XDimension, furni.YDimension, furni.Height, direction, altitude, false, "0"));
        }

        var spawns = (file.Spawns ?? []).ToDictionary(
            team => int.Parse(team.Key),
            team => (IReadOnlyList<(int X, int Y)>)team.Value.Where(tile => tile.Length == 2).Select(tile => (tile[0], tile[1])).ToImmutableArray());

        return new(file.FieldType, file.Name ?? string.Empty, new SnowStormLevelData(width, rows.Length, string.Join('\r', rows), objects.ToImmutable()), spawns);
    }

    private static IReadOnlyList<SnowStormArenaDefinition> Load(string directory, ILogger logger)
    {
        var arenas = new List<SnowStormArenaDefinition>();

        if (!Directory.Exists(directory)) {
            logger.LogWarning("SnowStorm arena directory {Directory} is missing", directory);

            return arenas;
        }

        foreach (var path in Directory.GetFiles(directory, "arena_*.json").Order(StringComparer.Ordinal)) {
            try {
                arenas.Add(Parse(File.ReadAllText(path)));
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException) {
                logger.LogError(exception, "Skipping SnowStorm arena {Path}", path);
            }
        }

        return arenas;
    }

    private sealed class ArenaFile
    {
        public int FieldType { get; set; }
        public string? Name { get; set; }
        public string[]? Heightmap { get; set; }
        public string[]? Items { get; set; }
        public Dictionary<string, int[][]>? Spawns { get; set; }
    }
}
