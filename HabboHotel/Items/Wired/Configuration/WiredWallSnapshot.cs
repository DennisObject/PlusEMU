using System.Globalization;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Raw wall placement: PixelY is a legacy renderer offset, not room altitude.</summary>
public sealed record WiredWallSnapshot(int TileX, int TileY, int LocalX, int PixelY, bool Left, int? NativeAltitude)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? CapturedAltitudeHundredths { get; init; }

    public bool IsWithinLimits() => TileX is >= -1000 and <= 700 && TileY is >= -1 and <= 700
        && LocalX is >= -1 and <= 700
        && (NativeAltitude is not null || PixelY is >= -1000 and <= 700);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $":w={TileX},{TileY} l={LocalX},{PixelY} {(Left ? "l" : "r")}")
        + (NativeAltitude is { } altitude ? string.Create(CultureInfo.InvariantCulture, $" a={altitude}") : string.Empty);

    internal static bool TryParse(string? text, out WiredWallSnapshot? position)
    {
        position = null;

        if (text == null) {
            return false;
        }

        var sections = text.Split(' ');

        if (sections.Length is not (3 or 4) || !sections[0].StartsWith(":w=", StringComparison.Ordinal)
            || !sections[1].StartsWith("l=", StringComparison.Ordinal) || sections[2] is not ("l" or "r")) {
            return false;
        }

        var xy = sections[0][3..].Split(',');
        var local = sections[1][2..].Split(',');

        if (xy.Length != 2 || local.Length != 2
            || !int.TryParse(xy[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(xy[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var y)
            || !int.TryParse(local[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var localX)
            || !int.TryParse(local[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var pixelY)) {
            return false;
        }

        int? nativeAltitude = null;

        if (sections.Length == 4) {
            if (!sections[3].StartsWith("a=", StringComparison.Ordinal)
                || !int.TryParse(sections[3][2..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var altitude)) {
                return false;
            }

            nativeAltitude = altitude;
        }

        var parsed = new WiredWallSnapshot(x, y, localX, pixelY, sections[2] == "l", nativeAltitude);

        if (!parsed.IsWithinLimits()) {
            return false;
        }

        position = parsed;

        return true;
    }
}
