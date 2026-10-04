using System.Text.Json;
using System.Text.Json.Nodes;

namespace Plus.HabboHotel.Items.Editor;

// The furni editor's furnidata update: { name?, description?, structure? }. Structure pushes furniture values into
// the entry and may only touch fields the entry already has (checked when it is applied).
public sealed record FurnidataEditPayload(string? Name, string? Description, IReadOnlyDictionary<string, JsonNode> Structure)
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 512;

    public bool HasText => Name != null || Description != null;

    public static (FurnidataEditPayload? Payload, string? Error) Parse(string json)
    {
        if (json.Length > FurniEditorUpdatePayload.MaxJsonLength)
            return (null, "Update is too large");
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }
        if (root == null)
            return (null, "Invalid JSON data");
        if (!Text(root, "name", MaxNameLength, out var name) || !Text(root, "description", MaxDescriptionLength, out var description))
            return (null, "Invalid name or description");
        var structure = new Dictionary<string, JsonNode>();
        if (root["structure"] is { } block)
        {
            if (block is not JsonObject fields)
                return (null, "structure must be an object");
            foreach (var (key, value) in fields)
            {
                if (StructureValue(key, value) is not { } node)
                    return (null, $"Invalid structure field: {key}");
                structure[key] = node;
            }
        }
        if (name == null && description == null && structure.Count == 0)
            return (null, "No name, description or structure provided");
        return (new(name, description, structure), null);
    }

    private static bool Text(JsonObject root, string key, int maxLength, out string? value)
    {
        value = null;
        if (!root.ContainsKey(key))
            return true;
        if (root[key] is not JsonValue node || !node.TryGetValue<string>(out var text) || text.Length > maxLength || text.Any(char.IsControl))
            return false;
        value = text;
        return true;
    }

    private static JsonNode? StructureValue(string key, JsonNode? value)
    {
        if (value is not JsonValue node)
            return null;
        return key switch
        {
            "xdim" or "ydim" when node.TryGetValue<int>(out var size) && size is >= 1 and <= 64 => JsonValue.Create(size),
            "height" when node.TryGetValue<double>(out var height) && double.IsFinite(height) && height is >= 0 and <= 99.99 => JsonValue.Create(height),
            "canstandon" or "cansiton" or "canlayon" when node.TryGetValue<bool>(out var flag) => JsonValue.Create(flag),
            _ => null
        };
    }
}
