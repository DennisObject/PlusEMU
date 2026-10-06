using System.Globalization;

namespace Plus.HabboHotel.Items;

internal static class FurnitureNumbers
{
    public static double Parse(string value) =>
        double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static int ParseInt(string value) =>
        int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);

    public static double FromCell(object value) =>
        Convert.ToDouble(value, CultureInfo.InvariantCulture);

    public static bool BooleanFromCell(object value) => value switch
    {
        bool boolean => boolean,
        string text => text == "1" || bool.TryParse(text, out var boolean) && boolean,
        _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0
    };
}
