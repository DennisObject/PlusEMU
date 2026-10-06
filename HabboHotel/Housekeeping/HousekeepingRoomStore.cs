using Dapper;
using Plus.Database;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Housekeeping;

public interface IHousekeepingRoomStore
{
    HousekeepingRoom? Find(int roomId);
    IReadOnlyList<HousekeepingRoom> Search(string query, bool exactMatch, int limit);
}

public sealed class HousekeepingRoomStore : IHousekeepingRoomStore
{
    private const string Select =
        "SELECT r.`id` AS Id, r.`caption` AS Name, COALESCE(r.`description`, '') AS Description, r.`owner` AS OwnerId, COALESCE(u.`username`, '') AS OwnerName, " +
        "r.`users_now` AS UserCount, r.`users_max` AS MaxUsers, r.`state` AS State, r.`roomtype` AS RoomType " +
        "FROM `rooms` r LEFT JOIN `users` u ON u.`id` = r.`owner` ";

    private readonly IDatabase _database;
    private readonly IRoomManager _roomManager;

    public HousekeepingRoomStore(IDatabase database, IRoomManager roomManager)
    {
        _database = database;
        _roomManager = roomManager;
    }

    public HousekeepingRoom? Find(int roomId)
    {
        if (roomId <= 0) {
            return null;
        }

        using var connection = _database.Connection();
        var row = connection.QuerySingleOrDefault<RoomRow>(Select + "WHERE r.`id` = @roomId LIMIT 1", new { roomId });

        return row == null ? null : View(row);
    }

    public IReadOnlyList<HousekeepingRoom> Search(string query, bool exactMatch, int limit)
    {
        using var connection = _database.Connection();
        var rows = connection.Query<RoomRow>(Select + (exactMatch ? "WHERE r.`caption` = @query " : "WHERE r.`caption` LIKE @query ") + "ORDER BY r.`id` LIMIT @limit",
            new { query = exactMatch ? query : EscapeLike(query) + "%", limit = Math.Clamp(limit, 1, HousekeepingLimits.MaxRoomResults) });

        return rows.Select(View).ToList();
    }

    internal static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>A loaded room's live state wins over the persisted row.</summary>
    private HousekeepingRoom View(RoomRow row)
    {
        if (_roomManager.TryGetRoom((uint)row.Id, out var room)) {
            return View(room);
        }

        return new(row.Id, row.Name, row.Description, row.OwnerId, row.OwnerName, row.UserCount, row.MaxUsers,
            !string.Equals(row.State, "open", StringComparison.OrdinalIgnoreCase), false, string.Equals(row.RoomType, "public", StringComparison.OrdinalIgnoreCase), 0);
    }

    // Plus has no room creation timestamp, so createdAt is always 0.
    internal static HousekeepingRoom View(Room room) =>
        new((int)room.Id, room.Name ?? string.Empty, room.Description ?? string.Empty, room.OwnerId, room.OwnerName ?? string.Empty, room.UsersNow, room.UsersMax,
            room.Access != RoomAccess.Open, room.RoomMuted, string.Equals(room.Type, "public", StringComparison.OrdinalIgnoreCase), 0);

    private sealed class RoomRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int OwnerId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public int UserCount { get; set; }
        public int MaxUsers { get; set; }
        public string State { get; set; } = "open";
        public string RoomType { get; set; } = "private";
    }
}
