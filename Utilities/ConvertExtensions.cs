namespace Plus.Utilities;

public static class ConvertExtensions
{
    public static string ToStringEnumValue(bool value) => value ? "1" : "0";

    public static bool EnumToBool(string value) => value == "1" || bool.TryParse(value, out var boolean) && boolean;

    public static int ToInt32(this bool value) => value ? 1 : 0;
}
