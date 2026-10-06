namespace Plus.HabboHotel.Rooms;

/// <summary>
/// Validates a floor-plan editor save before it is written.
/// The stored map is the editor map itself. Appending a blocked row makes the
/// editor send that row back, so every save grows the room.
/// </summary>
public static class FloorPlanSave
{
    public const int MaxAxis = 64;

    public const string ErrorTitle = "notification.floorplan_editor.error.title";
    public const string ErrorEffectiveHeight = "notification.floorplan_editor.error.message.effective_height_is_0";
    public const string ErrorTooLargeHeight = "notification.floorplan_editor.error.message.too_large_height";
    public const string ErrorTooLargeWidth = "notification.floorplan_editor.error.message.too_large_width";
    public const string ErrorEntryOutside = "notification.floorplan_editor.error.message.entry_tile_outside_map";
    public const string ErrorEntryNotOnTile = "notification.floorplan_editor.error.message.entry_not_on_tile";
    public const string ErrorEntryDirection = "notification.floorplan_editor.error.message.invalid_entry_tile_direction";
    public const string ErrorWallThickness = "notification.floorplan_editor.error.message.invalid_wall_thickness";
    public const string ErrorFloorThickness = "notification.floorplan_editor.error.message.invalid_floor_thickness";
    public const string ErrorWallHeight = "notification.floorplan_editor.error.message.invalid_walls_fixed_height";
    public const string ErrorBlockedByItem = "notification.floorplan_editor.error.message.change_blocked_by_room_item";

    public readonly record struct FloorPlanItem(uint Id, int X, int Y, int Rotation, int Width, int Length);

    public readonly record struct FloorTile(int Height, bool Walkable);

    public readonly record struct Layout(
        int DoorX,
        int DoorY,
        int DoorDirection,
        int WallThickness,
        int FloorThickness,
        int WallHeight);

    /// <summary>
    /// Map-only keeps the room's door, both thicknesses, and wall height.
    /// class_2506 writes the wall-height int only when that argument is not -1.
    /// Main Save passes -1 with the checkbox off. Import Save omits the argument,
    /// so the default -1 applies. Those two packets are the same five ints, and
    /// the missing int is stored as -1.
    /// </summary>
    public static Layout Resolve(bool doorFieldsPresent, bool wallHeightPresent, Layout requested, Layout existing)
    {
        if (!doorFieldsPresent)
        {
            return existing;
        }

        return wallHeightPresent ? requested : requested with
        {
            WallHeight = -1
        };
    }

    public readonly record struct Decision(
        bool Accepted,
        string? Error,
        string Map,
        int DoorX,
        int DoorY,
        int DoorZ,
        int DoorDirection,
        int WallThickness,
        int FloorThickness,
        int WallHeight,
        IReadOnlyList<uint> BlockingItemIds);

    public static Decision Evaluate(
        string? rawMap,
        int doorX,
        int doorY,
        int doorDirection,
        int wallThickness,
        int floorThickness,
        int wallHeight,
        IReadOnlyList<FloorPlanItem> items,
        IReadOnlyDictionary<(int X, int Y), FloorTile> currentTiles)
    {
        var map = rawMap?.ToLower().TrimEnd() ?? "";

        if (map.Length == 0 || map.Any(letter => !IsMapChar(letter)))
        {
            return Reject(ErrorTitle);
        }

        var rows = map.Split('\r');
        var width = rows[0].Length;

        if (rows.Length > MaxAxis)
        {
            return Reject(ErrorTooLargeHeight);
        }

        if (width == 0 || width > MaxAxis || rows.Any(row => row.Length == 0 || row.Length > MaxAxis))
        {
            return Reject(ErrorTooLargeWidth);
        }

        if (rows.Any(row => row.Length != width))
        {
            return Reject(ErrorTitle);
        }

        if (!rows.Any(row => row.Any(square => square != 'x')))
        {
            return Reject(ErrorEffectiveHeight);
        }

        if (doorX < 0 || doorY < 0 || doorX >= width || doorY >= rows.Length)
        {
            return Reject(ErrorEntryOutside);
        }

        if (rows[doorY][doorX] == 'x' || !TryHeight(rows[doorY][doorX], out var doorZ))
        {
            return Reject(ErrorEntryNotOnTile);
        }

        if (doorDirection < 0 || doorDirection > 7)
        {
            return Reject(ErrorEntryDirection);
        }

        if (wallThickness < -2 || wallThickness > 1)
        {
            return Reject(ErrorWallThickness);
        }

        if (floorThickness < -2 || floorThickness > 1)
        {
            return Reject(ErrorFloorThickness);
        }

        if (wallHeight < -1 || wallHeight > 15)
        {
            return Reject(ErrorWallHeight);
        }

        var blocking = new List<uint>();

        foreach (var item in items)
        {
            if (!ItemBlocksSave(item, rows, width, currentTiles))
            {
                continue;
            }

            blocking.Add(item.Id);
        }

        if (blocking.Count > 0)
        {
            return new Decision(false, ErrorBlockedByItem, map, doorX, doorY, doorZ, doorDirection, wallThickness, floorThickness, wallHeight, blocking);
        }

        return new Decision(true, null, map, doorX, doorY, doorZ, doorDirection, wallThickness, floorThickness, wallHeight, Array.Empty<uint>());
    }

    public static IReadOnlyList<(int X, int Y)> OccupiedTiles(IEnumerable<FloorPlanItem> items)
    {
        var seen = new HashSet<(int X, int Y)>();
        var tiles = new List<(int X, int Y)>();

        foreach (var item in items)
        {
            foreach (var tile in Tiles(item))
            {
                if (seen.Add(tile))
                {
                    tiles.Add(tile);
                }
            }
        }

        return tiles;
    }

    public static IEnumerable<(int X, int Y)> Tiles(FloorPlanItem item)
    {
        yield return (item.X, item.Y);

        foreach (var tile in Gamemap.GetAffectedTiles(item.Length, item.Width, item.X, item.Y, item.Rotation).Values)
        {
            yield return (tile.X, tile.Y);
        }
    }

    private static bool ItemBlocksSave(FloorPlanItem item, string[] rows, int width, IReadOnlyDictionary<(int X, int Y), FloorTile> currentTiles)
    {
        foreach (var (x, y) in Tiles(item))
        {
            if (x < 0 || y < 0 || x >= width || y >= rows.Length)
            {
                return true;
            }

            var square = rows[y][x];

            if (square == 'x')
            {
                return true;
            }

            if (currentTiles != null
                && currentTiles.TryGetValue((x, y), out var old)
                && old.Walkable
                && TryHeight(square, out var height)
                && height != old.Height)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMapChar(char letter) =>
        letter is (>= '0' and <= '9') or (>= 'a' and <= 'w') or 'x' or '\r';

    private static bool TryHeight(char square, out int height)
    {
        if (square is >= '0' and <= '9')
        {
            height = square - '0';

            return true;
        }

        if (square is >= 'a' and <= 'w')
        {
            height = square - 'a' + 10;

            return true;
        }

        height = 0;

        return false;
    }

    private static Decision Reject(string error) =>
        new(false, error, "", 0, 0, 0, 0, 0, 0, 0, Array.Empty<uint>());
}
