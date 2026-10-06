using System.Globalization;

namespace Plus.Utilities;

public static class TextHandling
{
    public static string GetString(double k) => k.ToString(CultureInfo.InvariantCulture);
}
