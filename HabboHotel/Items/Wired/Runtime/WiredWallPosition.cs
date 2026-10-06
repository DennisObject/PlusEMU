using System.Globalization;

namespace Plus.HabboHotel.Items.Wired.Runtime;

// The existing client/placement format stores whole wall units, not floor coordinates.
internal readonly record struct WiredWallPosition(int X, int Y, int Offset, int Altitude, bool Left)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $":w={X},{Y} l={Offset},{Altitude} {(Left ? "l" : "r")}");

    internal static bool TryParse(string? validated, out WiredWallPosition position)
    {
        position = default;

        if (validated == null)
        {
            return false;
        }

        var sections = validated.Split(' ');

        if (sections.Length != 3 || !sections[0].StartsWith(":w=") || !sections[1].StartsWith("l=")
            || sections[2] is not ("l" or "r"))
        {
            return false;
        }

        var xy = sections[0][3..].Split(',');
        var local = sections[1][2..].Split(',');

        if (xy.Length != 2 || local.Length != 2
            || !int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(local[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
            || !int.TryParse(local[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var altitude))
        {
            return false;
        }

        position = new(x, y, offset, altitude, sections[2] == "l");

        return true;
    }
}
