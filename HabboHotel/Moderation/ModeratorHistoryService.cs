using System.Collections.Immutable;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Logs;

namespace Plus.HabboHotel.Moderation;

public interface IModeratorHistoryService
{
    ModeratorRoomChatlog? GetRoomChatlog(uint roomId);
    ModeratorUserChatlog? GetUserChatlog(int userId);
    ModeratorUserRoomVisits? GetUserRoomVisits(int userId);
}

public interface IModeratorUserLookup
{
    Users.Habbo? GetById(int userId);
}

public sealed class ModeratorUserLookup : IModeratorUserLookup
{
    public Users.Habbo? GetById(int userId) => PlusEnvironment.GetHabboById(userId);
}

public sealed class ModeratorHistoryService(
    IDatabase database,
    IRoomManager roomManager,
    IModeratorUserLookup userLookup,
    IChatlogManager chatlogManager,
    TimeProvider timeProvider) : IModeratorHistoryService
{
    public ModeratorRoomChatlog? GetRoomChatlog(uint roomId)
    {
        if (!roomManager.TryGetRoom(roomId, out var room)) return null;
        chatlogManager.FlushAndSave();
        using var connection = database.Connection();
        var entries = ResolveEntries(connection.Query<ChatlogRow>(
            "SELECT user_id AS UserId, `timestamp` AS Timestamp, message FROM chatlogs WHERE room_id=@roomId ORDER BY id DESC LIMIT 100", new { roomId }));
        return new(new(room.Id, room.Name), entries);
    }

    public ModeratorUserChatlog? GetUserChatlog(int userId)
    {
        var user = GetOnlineUser(userId);
        if (user == null) return null;
        chatlogManager.FlushAndSave();
        using var connection = database.Connection();
        var visits = connection.Query<RoomVisitRow>(
            """
            SELECT visits.room_id AS RoomId, rooms.caption AS RoomName,
                   visits.entry_timestamp AS EntryTimestamp, visits.exit_timestamp AS ExitTimestamp
            FROM (
                SELECT room_id, entry_timestamp, exit_timestamp FROM user_roomvisits
                WHERE user_id=@userId ORDER BY entry_timestamp DESC LIMIT 7
            ) AS visits
            LEFT JOIN rooms ON rooms.id=visits.room_id
            """, new { userId });
        var rooms = new List<ModeratorRoomChatlog>();
        var now = timeProvider.GetUtcNow();
        foreach (var visit in visits)
        {
            if (visit.RoomName == null) continue;
            var exit = visit.ExitTimestamp is > 0 ? visit.ExitTimestamp.Value : ToUnixTime(now);
            var entries = ResolveEntries(connection.Query<ChatlogRow>(
                """
                SELECT user_id AS UserId, `timestamp` AS Timestamp, message FROM chatlogs
                WHERE room_id=@RoomId AND `timestamp`>@EntryTimestamp AND `timestamp`<@ExitTimestamp
                ORDER BY `timestamp` DESC LIMIT 100
                """, new { visit.RoomId, visit.EntryTimestamp, ExitTimestamp = exit }));
            rooms.Add(new(new(visit.RoomId, visit.RoomName), entries));
        }
        return new(new(user.Id, user.Username), rooms.ToImmutableArray());
    }

    public ModeratorUserRoomVisits? GetUserRoomVisits(int userId)
    {
        var user = GetOnlineUser(userId);
        if (user == null) return null;
        using var connection = database.Connection();
        var rows = connection.Query<RoomVisitSummaryRow>(
            """
            SELECT visits.room_id AS RoomId, rooms.caption AS RoomName, visits.entry_timestamp AS EntryTimestamp
            FROM (
                SELECT room_id, entry_timestamp FROM user_roomvisits
                WHERE user_id=@userId ORDER BY entry_timestamp DESC LIMIT 50
            ) AS visits
            LEFT JOIN rooms ON rooms.id=visits.room_id
            """, new { userId });
        var timestamps = new HashSet<double>();
        var visits = new List<ModeratorRoomVisit>();
        foreach (var row in rows)
            if (row.RoomName != null && timestamps.Add(row.EntryTimestamp))
                visits.Add(new(new(row.RoomId, row.RoomName), FromUnixTime(row.EntryTimestamp)));
        return new(new(user.Id, user.Username), visits.ToImmutableArray());
    }

    private ImmutableArray<ModeratorChatEntry> ResolveEntries(IEnumerable<ChatlogRow> rows)
    {
        var entries = new List<ModeratorChatEntry>();
        foreach (var row in rows)
        {
            var user = GetOnlineUser(row.UserId);
            if (user != null) entries.Add(new(row.UserId, user.Username, row.Message, FromUnixTime(row.Timestamp)));
        }
        return entries.ToImmutableArray();
    }

    private Users.Habbo? GetOnlineUser(int userId) => userLookup.GetById(userId);
    private static DateTimeOffset FromUnixTime(double value) => DateTimeOffset.UnixEpoch.AddMilliseconds(value * 1000d);
    private static double ToUnixTime(DateTimeOffset value) => value.ToUnixTimeMilliseconds() / 1000d;

    private sealed record ChatlogRow(int UserId, double Timestamp, string Message);
    private sealed record RoomVisitRow(uint RoomId, string? RoomName, double EntryTimestamp, double? ExitTimestamp);
    private sealed record RoomVisitSummaryRow(uint RoomId, string? RoomName, double EntryTimestamp);
}

public sealed record ModeratorUserIdentity(int Id, string Username);
public sealed record ModeratorRoomIdentity(uint Id, string Name);
public sealed record ModeratorChatEntry(int UserId, string Username, string Message, DateTimeOffset Timestamp);
public sealed record ModeratorRoomChatlog(ModeratorRoomIdentity Room, ImmutableArray<ModeratorChatEntry> Entries);
public sealed record ModeratorUserChatlog(ModeratorUserIdentity User, ImmutableArray<ModeratorRoomChatlog> Rooms);
public sealed record ModeratorRoomVisit(ModeratorRoomIdentity Room, DateTimeOffset EnteredAt);
public sealed record ModeratorUserRoomVisits(ModeratorUserIdentity User, ImmutableArray<ModeratorRoomVisit> Visits);
