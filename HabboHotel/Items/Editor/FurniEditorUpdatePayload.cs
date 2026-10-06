using System.Globalization;
using System.Text.Json;

namespace Plus.HabboHotel.Items.Editor;

// Validates the furni editor's JSON update. The editor sends its whole form, so only fields that differ from the
// stored row become changes; unknown fields are ignored. Columns come from the fixed map below, never from input.
public static class FurniEditorUpdatePayload
{
    public const int MaxJsonLength = 8192;
    private const string Unsupported = "is not supported by this hotel";

    private static readonly Dictionary<string, string> FlagColumns = new()
    {
        ["allowStack"] = "can_stack",
        ["allowWalk"] = "is_walkable",
        ["allowSit"] = "can_sit",
        ["allowGift"] = "allow_gift",
        ["allowTrade"] = "allow_trade",
        ["allowRecycle"] = "allow_recycle",
        ["allowMarketplaceSell"] = "allow_marketplace_sell",
        ["allowInventoryStack"] = "allow_inventory_stack"
    };

    public static (List<FurniEditorColumnChange> Changes, string? Error) Validate(string json, FurniEditorItem current, Func<string, bool> knownInteraction)
    {
        if (json.Length > MaxJsonLength)
        {
            return ([], "Update is too large");
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return ([], "Invalid JSON data");
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return ([], "Invalid JSON data");
        }

        var changes = new List<FurniEditorColumnChange>();
        int? effectMale = null, effectFemale = null;

        foreach (var property in root.EnumerateObject())
        {
            string field = property.Name;
            var value = property.Value;
            string? error = field switch
            {
                "publicName" => Text(changes, field, "public_name", value, current.PublicName, 56),
                "width" => Int(changes, field, "width", value, current.Width, 1, 64),
                "length" => Int(changes, field, "length", value, current.Length, 1, 64),
                "stackHeight" => Height(changes, value, current.StackHeight),
                "interactionType" => Interaction(changes, value, current.InteractionType, knownInteraction),
                "interactionModesCount" => Int(changes, field, "interaction_modes_count", value, current.InteractionModesCount, 0, 100),
                "vendingIds" => NumberList(changes, field, "vending_ids", value, current.VendingIds, 255, integers: true),
                "multiheight" => NumberList(changes, field, "height_adjustable", value, current.Multiheight, 50, integers: false),
                "effectIdMale" => ReadEffect(value, out effectMale),
                "effectIdFemale" => ReadEffect(value, out effectFemale),
                "allowLay" => Bool(value) is { } lay ? (lay == current.AllowLay ? null : $"{field} {Unsupported}") : $"Invalid value for {field}",
                "customparams" => Same(field, value, current.CustomParams),
                "clothingOnWalk" => Same(field, value, current.ClothingOnWalk),
                _ when FlagColumns.TryGetValue(field, out var column) => Flag(changes, field, column, value, FlagValue(current, field)),
                _ => null
            };

            if (error != null)
            {
                return ([], error);
            }
        }

        if (Effect(changes, effectMale, effectFemale, current.EffectId) is { } effectError)
        {
            return ([], effectError);
        }

        return (changes, null);
    }

    private static bool FlagValue(FurniEditorItem item, string field) => field switch
    {
        "allowStack" => item.AllowStack,
        "allowWalk" => item.AllowWalk,
        "allowSit" => item.AllowSit,
        "allowGift" => item.AllowGift,
        "allowTrade" => item.AllowTrade,
        "allowRecycle" => item.AllowRecycle,
        "allowMarketplaceSell" => item.AllowMarketplaceSell,
        _ => item.AllowInventoryStack
    };

    private static string? Text(List<FurniEditorColumnChange> changes, string field, string column, JsonElement value, string current, int maxLength)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length > maxLength || text.Any(char.IsControl))
        {
            return $"Invalid value for {field}";
        }

        if (text != current)
        {
            changes.Add(new(field, column, text, current));
        }

        return null;
    }

    private static string? Int(List<FurniEditorColumnChange> changes, string field, string column, JsonElement value, int current, int min, int max)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max)
        {
            return $"Invalid value for {field}";
        }

        if (number != current)
        {
            changes.Add(new(field, column, number, current));
        }

        return null;
    }

    private static string? Height(List<FurniEditorColumnChange> changes, JsonElement value, double current)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var height) || !double.IsFinite(height) || height is < 0 or > 99.99)
        {
            return "Invalid value for stackHeight";
        }

        height = Math.Round(height, 2);

        if (Math.Abs(height - current) > 0.0001)
        {
            changes.Add(new("stackHeight", "stack_height", height, current));
        }

        return null;
    }

    // An interaction without a handler would quietly behave as plain furniture once loaded, so it is refused.
    private static string? Interaction(List<FurniEditorColumnChange> changes, JsonElement value, string current, Func<string, bool> knownInteraction)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } raw)
        {
            return "Invalid value for interactionType";
        }

        var type = raw.Trim().ToLowerInvariant();

        if (type.Length == 0)
        {
            type = "default";
        }

        if (type == current.ToLowerInvariant())
        {
            return null;
        }

        if (type.Length > 25 || !knownInteraction(type))
        {
            return $"Unknown interaction type: {raw}";
        }

        changes.Add(new("interactionType", "interaction_type", type, current));

        return null;
    }

    // Comma separated numbers; the item loader parses every entry, so anything else is refused here.
    private static string? NumberList(List<FurniEditorColumnChange> changes, string field, string column, JsonElement value, string current, int maxLength, bool integers)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } raw)
        {
            return $"Invalid value for {field}";
        }

        var text = raw.Replace(" ", string.Empty);

        if (text == current.Replace(" ", string.Empty))
        {
            return null;
        }

        if (text.Length == 0)
        {
            text = "0";
        }

        if (text.Length > maxLength)
        {
            return $"{field} is limited to {maxLength} characters";
        }

        foreach (var entry in text.Split(','))
        {
            bool valid = integers
                ? int.TryParse(entry, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                : double.TryParse(entry, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) && number <= 99.99;

            if (!valid)
            {
                return $"Invalid value for {field}: use comma separated {(integers ? "whole numbers" : "heights")}";
            }
        }

        if (text != current)
        {
            changes.Add(new(field, column, text, current));
        }

        return null;
    }

    private static string? Flag(List<FurniEditorColumnChange> changes, string field, string column, JsonElement value, bool current)
    {
        if (Bool(value) is not { } flag)
        {
            return $"Invalid value for {field}";
        }

        if (flag != current)
        {
            changes.Add(new(field, column, flag ? "1" : "0", current ? "1" : "0"));
        }

        return null;
    }

    private static bool? Bool(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number when value.TryGetInt32(out var number) && number is 0 or 1 => number == 1,
        JsonValueKind.String when value.GetString() is "0" or "1" => value.GetString() == "1",
        _ => null
    };

    private static string? Same(string field, JsonElement value, string current) =>
        value.ValueKind == JsonValueKind.String && (value.GetString() ?? string.Empty) == current ? null : $"{field} {Unsupported}";

    private static string? ReadEffect(JsonElement value, out int? effect)
    {
        effect = null;

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number is < 0 or > 999)
        {
            return "Invalid effect id";
        }

        effect = number;

        return null;
    }

    // PlusEMU has one effect id for both genders.
    private static string? Effect(List<FurniEditorColumnChange> changes, int? male, int? female, int current)
    {
        if (male != null && female != null && male != female)
        {
            return "This hotel uses one effect id for both genders";
        }

        var effect = male ?? female;

        if (effect != null && effect != current)
        {
            changes.Add(new("effectId", "effect_id", effect.Value, current));
        }

        return null;
    }
}
