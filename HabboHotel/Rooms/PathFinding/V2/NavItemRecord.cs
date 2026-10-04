using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Immutable geometry. Footprints are copied; no compiler reads a live Item.
public sealed record NavItemRecord(uint ItemId, long Version, double Z, double Height,
    bool Walkable, bool Seat, InteractionType Interaction, string State, int GroupId,
    int Rotation, bool Removed, IReadOnlyList<int> Footprint,
    int X = 0, int Y = 0, int Length = 1, int Width = 1, WiredBoxType WiredType = WiredBoxType.None)
{
    public double Top => Z + Height;
    internal static bool StateRelevant(ItemDefinition definition) => definition.InteractionType is InteractionType.Gate or InteractionType.GuildGate
        || definition.AdjustableHeights is { Count: > 1 };

    internal static NavItemRecord Capture(Item item, long version, int width, int height, NavItemRecord? previous = null)
    {
        var definition = item.Definition;
        var currentHeight = definition.Height;
        if (definition.AdjustableHeights is { Count: > 1 } heights
            && int.TryParse(item.LegacyDataString, out var index) && index >= 0 && index < heights.Count
            && item.GetZ + heights[index] > 0)
            currentHeight = heights[index];
        // Only gate state changes standability; adjustable state is represented by Height.
        var state = definition.InteractionType is InteractionType.Gate or InteractionType.GuildGate ? item.LegacyDataString : "";
        var group = definition.InteractionType == InteractionType.GuildGate ? item.GroupId : 0;
        var sameFootprint = previous != null && previous.X == item.GetX && previous.Y == item.GetY
            && previous.Rotation == item.Rotation && previous.Length == definition.Length && previous.Width == definition.Width;
        if (sameFootprint && !previous!.Removed && previous.Z == item.GetZ && previous.Height == currentHeight
            && previous.Walkable == definition.Walkable && previous.Seat == definition.IsSeat
            && previous.Interaction == definition.InteractionType && previous.State == state && previous.GroupId == group
            && previous.WiredType == definition.WiredType)
            return previous;
        var footprint = sameFootprint ? previous!.Footprint : Array.AsReadOnly(Gamemap.GetAffectedTiles(definition.Length, definition.Width,
            item.GetX, item.GetY, item.Rotation).Values
            .Select(p => (p.X, p.Y)).Append((item.GetX, item.GetY))
            .Where(p => p.Item1 >= 0 && p.Item1 < width && p.Item2 >= 0 && p.Item2 < height)
            .Select(p => p.Item2 * width + p.Item1).Distinct().Order().ToArray());
        return new(item.Id, version, item.GetZ, currentHeight,
            definition.Walkable, definition.IsSeat, definition.InteractionType,
            state, group, item.Rotation, false, footprint, item.GetX, item.GetY, definition.Length, definition.Width, definition.WiredType);
    }
}
