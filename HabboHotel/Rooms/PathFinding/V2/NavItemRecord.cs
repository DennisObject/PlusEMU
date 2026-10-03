using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Immutable geometry. Footprints are copied; no compiler reads a live Item.
public sealed record NavItemRecord(uint ItemId, long Version, double Z, double Height,
    bool Walkable, bool Seat, InteractionType Interaction, string State, int GroupId,
    int Rotation, bool Removed, IReadOnlyList<int> Footprint)
{
    public double Top => Z + Height;

    internal static NavItemRecord Capture(Item item, long version, int width, int height)
    {
        var tiles = Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width,
            item.GetX, item.GetY, item.Rotation).Values
            .Select(p => (p.X, p.Y)).Append((item.GetX, item.GetY))
            .Where(p => p.Item1 >= 0 && p.Item1 < width && p.Item2 >= 0 && p.Item2 < height)
            .Select(p => p.Item2 * width + p.Item1).Distinct().Order().ToArray();
        var currentHeight = item.Definition.Height;
        if (item.Definition.AdjustableHeights is { Count: > 1 } heights
            && int.TryParse(item.LegacyDataString, out var state) && state >= 0 && state < heights.Count
            && item.GetZ + heights[state] > 0)
            currentHeight = heights[state];
        return new(item.Id, version, item.GetZ, currentHeight,
            item.Definition.Walkable, item.Definition.IsSeat, item.Definition.InteractionType,
            item.LegacyDataString, item.GroupId, item.Rotation, false, Array.AsReadOnly(tiles));
    }
}
