using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

// §6.6 contact ownership. In layering mode hooks reach only the items owned by the actor's surface
// (its owned items plus its support item). K=1, and tiles without surfaces, keep legacy dispatch:
// every item on the tile, in the same order.
internal static class SurfaceContacts
{
    internal static int ContactSlot(NavGrid? grid, int x, int y, SurfaceRef? surface, double z)
    {
        if (grid is not { Layered: true } || !grid.InBounds(x, y)) return -1;
        if (surface is { } current && grid.SlotOf(current) is var slot and >= 0 && grid.Active(slot)) return slot;
        return SurfaceSelection.Resting(grid, grid.Tile(x, y), z);
    }

    internal static List<Item> Filter(NavGrid? grid, int x, int y, int contactSlot, IEnumerable<Item> items)
    {
        if (contactSlot < 0) return items.ToList();
        var tile = grid!.Tile(x, y);
        return items.Where(item => Owner(grid, tile, item) == contactSlot).ToList();
    }

    internal static List<Item> Of(Room room, RoomUser actor, IEnumerable<Item> items)
    {
        var grid = room.GetGameMap().Navigation?.Grid;
        var slot = ContactSlot(grid, actor.X, actor.Y, actor.Movement.CurrentRef, actor.Movement.SupportZ);
        return Filter(grid, actor.X, actor.Y, slot, items);
    }

    // Items published after the latest compile are owned by the surface they rest on.
    private static int Owner(NavGrid grid, int tile, Item item)
    {
        var owner = grid.OwnerOf(tile, item.Id);
        return owner >= 0 ? owner : SurfaceSelection.Resting(grid, tile, item.GetZ);
    }
}
