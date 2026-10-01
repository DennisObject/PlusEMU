using System.Globalization;
using Plus.HabboHotel.Catalog.Utilities;

namespace Plus.HabboHotel.Items;

internal static class GiftWrap
{
    public static string PresentData(string user, string message, int senderId, uint definitionId, int spriteId, int boxId, int ribbonId)
        => string.Join(((char)5).ToString(), user, message, senderId.ToString(CultureInfo.InvariantCulture), definitionId.ToString(CultureInfo.InvariantCulture), spriteId.ToString(CultureInfo.InvariantCulture), boxId.ToString(CultureInfo.InvariantCulture), ribbonId.ToString(CultureInfo.InvariantCulture));

    public static int Style(string legacy)
    {
        var fields = legacy.Split((char)5);
        if (fields.Length == 7
            && int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var color)
            && int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ribbon))
        {
            var style = (long)color * 1000 + ribbon;
            if (style is >= int.MinValue and <= int.MaxValue)
                return (int)style;
        }

        return 1;
    }

    public static bool PetDataAccepted(string data)
    {
        try
        {
            var bits = data.Split('\n');
            if (!PetUtility.CheckPetName(bits[0]))
                return false;
            if (bits[1].Length > 2)
                return false;
            if (bits[2].Length != 6)
                return false;
            return true;
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }
    }
}
