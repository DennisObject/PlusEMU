using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

internal static class FurniExtraData
{
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
