using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Plus.HabboHotel.Items.Editor;

public sealed record FurnidataLookup(string EntryJson, string DiagnosticJson);

// The entry before and after an edit (compact JSON) and what clients need to patch their copy.
public sealed record FurnidataEdit(string Before, string After, bool IsWallItem, int Id, string Classname, string Name, string Description)
{
    public bool Changed => Before != After;
}

public sealed class FurnidataException(string message) : Exception(message);

// Which furnidata entry a furniture row edits: its classname in its own section, told apart by sprite id when
// several entries share the classname.
public sealed record FurnidataTarget(string Classname, int Id, bool IsWallItem);

public interface IFurnidataStore
{
    FurnidataLookup Lookup(string classname, int spriteId);

    // Applies edit to the target entry and writes the file atomically.
    FurnidataEdit Edit(FurnidataTarget target, Action<JsonObject> edit);

    // Puts entryJson back, but only while the entry is still exactly expectedCurrent.
    FurnidataEdit Restore(FurnidataTarget target, string expectedCurrent, string entryJson);
}

// FurnitureData.json at the configured path (FurniEditor:FurnidataPath). The path never comes from a client and
// classnames are only compared with entries, so no input can point the writer at another file. Writes go to a
// temp file in the same directory, the old file is kept as <name>.bak, and the temp file is renamed over it.
public sealed class FurnidataStore : IFurnidataStore
{
    private static readonly string[] Sections = ["roomitemtypes", "wallitemtypes"];
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Indented = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    private readonly FurniEditorConfiguration _configuration;
    private readonly object _writeSync = new();
    private Index? _index;

    public FurnidataStore(IOptions<FurniEditorConfiguration> configuration) => _configuration = configuration.Value;

    public FurnidataLookup Lookup(string classname, int spriteId)
    {
        string path = _configuration.FurnidataPath;

        try
        {
            var source = Resolve();

            if (source == null)
            {
                return new("{}", Diagnostic("source_missing", spriteId, classname, path, string.IsNullOrWhiteSpace(path) ? "CONFIG_MISSING" : "MISSING", ""));
            }

            var index = IndexFor(source);
            var key = classname.Trim().ToLowerInvariant();

            if (index.ByClassname.TryGetValue(key, out var exact))
            {
                return new(exact, Diagnostic("matched_classname", spriteId, classname, path, "OK", ""));
            }

            int star = key.IndexOf('*');

            if (star > 0 && index.ByClassname.TryGetValue(key[..star], out var stripped))
            {
                return new(stripped, Diagnostic("matched_classname_stripped", spriteId, classname, path, "OK", ""));
            }

            if (index.ById.TryGetValue(spriteId, out var byId))
            {
                return new(byId, Diagnostic("matched_id", spriteId, classname, path, "OK", ""));
            }

            return new("{}", Diagnostic(index.Empty ? "manifest_empty" : "not_found", spriteId, classname, path, "OK", ""));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or FurnidataException)
        {
            // IO messages carry server paths; staff only learn that the file could not be read.
            return new("{}", Diagnostic("error", spriteId, classname, path, "ERROR", e is FurnidataException ? e.Message : "The furnidata file could not be read"));
        }
    }

    public FurnidataEdit Edit(FurnidataTarget target, Action<JsonObject> edit) => Write(target, entry =>
    {
        edit(entry);

        return entry;
    });

    public FurnidataEdit Restore(FurnidataTarget target, string expectedCurrent, string entryJson) => Write(target, current =>
    {
        if (current.ToJsonString(Compact) != expectedCurrent)
        {
            throw new FurnidataException("The furnidata entry changed since that edit; revert refused");
        }

        return JsonNode.Parse(entryJson) as JsonObject ?? throw new FurnidataException("The logged entry is not a JSON object");
    });

    private FurnidataEdit Write(FurnidataTarget target, Func<JsonObject, JsonObject> change)
    {
        if (string.IsNullOrWhiteSpace(target.Classname))
        {
            throw new FurnidataException("The furniture has no classname");
        }

        lock (_writeSync)
        {
            var source = Resolve() ?? throw new FurnidataException("Furnidata source not configured");
            var text = File.ReadAllText(source.FullName, Encoding.UTF8);
            var root = JsonNode.Parse(text) as JsonObject ?? throw new FurnidataException("Furnidata is not a JSON object");
            var (array, position) = Find(root, target);
            bool isWall = target.IsWallItem;
            var current = (JsonObject)array[position]!;
            var before = current.ToJsonString(Compact);
            var updated = change((JsonObject)JsonNode.Parse(before)!);

            if (!string.Equals(Text(updated["classname"]), Text(current["classname"]), StringComparison.OrdinalIgnoreCase) || Number(updated["id"]) != Number(current["id"]))
            {
                throw new FurnidataException("An edit cannot change the classname or id");
            }

            var after = updated.ToJsonString(Compact);
            var result = new FurnidataEdit(before, after, isWall, Number(updated["id"]), Text(updated["classname"]), Text(updated["name"]), Text(updated["description"]));

            if (!result.Changed)
            {
                return result;
            }

            array[position] = updated;
            var output = root.ToJsonString(text.Contains('\n') ? Indented : Compact);

            if (text.EndsWith('\n'))
            {
                output += "\n";
            }

            Replace(source, output);
            _index = null;

            return result;
        }
    }

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static int Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static (JsonArray Array, int Position) Find(JsonObject root, FurnidataTarget target)
    {
        if (root[target.IsWallItem ? "wallitemtypes" : "roomitemtypes"]?["furnitype"] is not JsonArray types)
        {
            throw new FurnidataException("No furnidata entry for this classname");
        }

        var matches = Enumerable.Range(0, types.Count)
            .Where(i => types[i] is JsonObject entry && string.Equals(Text(entry["classname"]), target.Classname, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count > 1)
        {
            matches = matches.Where(i => Number(types[i]!["id"]) == target.Id).ToList();

            if (matches.Count != 1)
            {
                throw new FurnidataException("Several furnidata entries share this classname and sprite id");
            }
        }

        return matches is [var position] ? (types, position) : throw new FurnidataException("No furnidata entry for this classname");
    }

    private void Replace(FileInfo target, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        if (bytes.LongLength > _configuration.FurnidataMaxBytes)
        {
            throw new FurnidataException("Furnidata would exceed the configured size limit");
        }

        var directory = target.DirectoryName!;
        var temp = Path.Combine(directory, $".{target.Name}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Copy(target.FullName, target.FullName + ".bak", overwrite: true);
            File.Move(temp, target.FullName, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // The configured file, following a symlink to the file it points at so the link itself survives the rename.
    private FileInfo? Resolve()
    {
        var configured = _configuration.FurnidataPath;

        if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured)
            || !configured.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var file = new FileInfo(Path.GetFullPath(configured));

        if (file.LinkTarget != null && file.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target)
        {
            file = target;
        }

        if (!file.Exists)
        {
            return null;
        }

        if (file.Length > _configuration.FurnidataMaxBytes)
        {
            throw new FurnidataException("Furnidata exceeds the configured size limit");
        }

        return file;
    }

    private Index IndexFor(FileInfo source)
    {
        var stamp = (source.FullName, source.LastWriteTimeUtc, source.Length);

        if (_index is { } cached && cached.Stamp == stamp)
        {
            return cached;
        }

        var byClassname = new Dictionary<string, string>();
        var byId = new Dictionary<int, string>();
        using var document = JsonDocument.Parse(File.ReadAllBytes(source.FullName));

        foreach (var section in Sections)
        {
            if (!document.RootElement.TryGetProperty(section, out var sectionElement) || !sectionElement.TryGetProperty("furnitype", out var types)
                || types.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in types.EnumerateArray())
            {
                var json = JsonSerializer.Serialize(entry, Compact);

                if (entry.TryGetProperty("classname", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    byClassname.TryAdd(name.GetString()!.Trim().ToLowerInvariant(), json);
                }

                if (entry.TryGetProperty("id", out var id) && id.TryGetInt32(out var number))
                {
                    byId.TryAdd(number, json);
                }
            }
        }

        var index = new Index(stamp, byClassname, byId, byClassname.Count == 0 && byId.Count == 0);
        _index = index;

        return index;
    }

    private static string Diagnostic(string reason, int itemId, string classname, string sourcePath, string status, string message) =>
        JsonSerializer.Serialize(new
        {
            reason,
            itemId,
            classname,
            sourcePath = Path.GetFileName(sourcePath),
            sourceDirectory = false,
            sourceStatus = status,
            message
        }, Compact);

    private sealed record Index((string, DateTime, long) Stamp, Dictionary<string, string> ByClassname, Dictionary<int, string> ById, bool Empty);
}
