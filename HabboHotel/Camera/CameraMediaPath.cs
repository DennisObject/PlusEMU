namespace Plus.HabboHotel.Camera;

internal static class CameraMediaPath
{
    public static string For(Guid id) => "/camera/" + id.ToString("D") + ".png";

    public static bool TryReadId(string? value, out Guid id)
    {
        id = Guid.Empty;
        if (value == null || value.Length is not (32 or 36)) return false;
        if (!Guid.TryParseExact(value, value.Length == 32 ? "N" : "D", out id) || id == Guid.Empty)
        {
            id = Guid.Empty;
            return false;
        }

        return true;
    }
}
