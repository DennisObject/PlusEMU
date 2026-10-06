using System.Globalization;
using Plus.HabboHotel.Items.DataFormat;

namespace Plus.HabboHotel.Items;

/// <summary>
/// Height rules for the stack-height widget: only magic tiles may be adjusted, "-100" matches the
/// stack below, and every result is clamped between the floor and the furniture height limit.
/// </summary>
internal static class MagicTileHeight
{
    internal const double MaximumHeight = 40.0;
    internal const int MatchBelow = -100;

    internal static bool IsMagicTile(InteractionType type) => type is InteractionType.Stacktool or InteractionType.WalkMagicTile;

    internal static double Resolve(int requested, double floorZ, double stackBelowZ)
    {
        var height = requested == MatchBelow ? stackBelowZ : requested / 100.0;

        return Clamp(height, floorZ);
    }

    internal static double Clamp(double height, double floorZ)
    {
        if (!double.IsFinite(height))
        {
            height = floorZ;
        }

        return Math.Clamp(height, floorZ, Math.Max(floorZ, MaximumHeight));
    }

    internal static bool MultiWalk(Item item) => item.LegacyDataString.Split(';').Skip(1).FirstOrDefault() == "1";

    internal static bool Sync(Item item)
    {
        if (!IsMagicTile(item.Definition.InteractionType))
        {
            return false;
        }

        var stored = item.LegacyDataString;
        var separator = stored.IndexOf(';');
        var normalized = ToWire(item.GetZ).ToString(CultureInfo.InvariantCulture)
            + (separator < 0 ? "" : stored[separator..]);

        if (stored == normalized && item.ExtraData is LegacyDataFormat)
        {
            return false;
        }

        item.ExtraData = new LegacyDataFormat { Data = normalized };

        return true;
    }

    internal static int ToWire(double height) => (int)Math.Round(height * 100.0);
}
