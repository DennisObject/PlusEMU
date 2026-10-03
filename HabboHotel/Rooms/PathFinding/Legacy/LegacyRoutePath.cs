namespace Plus.HabboHotel.Rooms.PathFinding;

// Legacy routes are reversed lists: goal first, start sentinel last, next step at Count - PathStep - 1.
internal static class LegacyRoutePath
{
    public static List<Vector2D> Remaining(RoomUser user)
    {
        var tiles = new List<Vector2D>();
        for (var step = user.PathStep; step < user.Path.Count; step++)
            tiles.Add(user.Path[user.Path.Count - step - 1]);
        return tiles;
    }

    // Replaces the path and its index together, with the current tile as the start sentinel.
    public static void InstallPrefix(RoomUser user, IReadOnlyList<Vector2D> tiles)
    {
        var path = new List<Vector2D>(tiles.Count + 1);
        for (var i = tiles.Count - 1; i >= 0; i--) path.Add(tiles[i]);
        path.Add(new(user.X, user.Y));
        Install(user, path);
    }

    public static void Install(RoomUser user, List<Vector2D> path)
    {
        user.Path = path;
        user.PathStep = 1;
    }
}
