using System.Text.RegularExpressions;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

internal static class FurniExtraData
{
    private static readonly HashSet<string> ClientImageKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "w", "url", "id", "imageUrl", "href", "src", "clickUrl"
    };

    private static readonly Regex ExternalAddress = new(
        @"(?:https?:|data:|blob:|javascript:|file:|//)|\u0000",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EmbeddedImageKey = new(
        "\"(?:w|url|id|imageUrl|href|src|clickUrl)\"\\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool RejectsClientImage(IReadOnlyList<string> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] ?? "";
            if (index % 2 == 0 && ClientImageKeys.Contains(value))
                return true;
            if (ExternalAddress.IsMatch(value) || EmbeddedImageKey.IsMatch(value))
                return true;
        }

        return false;
    }

    public static IFurniObjectData Load(ItemDefinition definition, string stored, bool keepLegacy)
    {
        stored ??= "";
        switch (definition.InteractionType)
        {
            case InteractionType.CrackableEgg:
            {
                var data = definition.CreateData();
                if (stored.Length > 0)
                    data.Store(stored);
                return data;
            }
            case InteractionType.Background:
            {
                var data = definition.CreateData();
                if (stored.Length > 0)
                    data.Store(BackgroundPairs(stored));
                return data;
            }
            default:
                return keepLegacy ? new LegacyDataFormat { Data = stored } : FurniObjectData.Empty;
        }
    }

    public static string BackgroundPairs(string stored)
    {
        if (stored.Contains('\n'))
            return stored;
        var fields = stored.Split('\t');
        if (fields.Length < 2 || fields.Length % 2 != 0)
            return stored;
        return string.Join("\n", Enumerable.Range(0, fields.Length / 2).Select(index => $"{fields[index * 2]}\t{fields[index * 2 + 1]}"));
    }

    public static string Branding(IReadOnlyList<string> values)
    {
        var fields = new List<string> { "state", "0" };
        fields.AddRange(values);
        var pairs = fields.Count / 2;
        return string.Join("\n", Enumerable.Range(0, pairs).Select(index => $"{fields[index * 2]}\t{fields[index * 2 + 1]}"));
    }
}
