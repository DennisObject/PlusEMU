using Dapper;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.Core.Language;
using Plus.Database;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms;

public interface IRoomRightsService
{
    void Show(GameClient session);
    void Assign(Room room, GameClient session, int userId);
    void Remove(Room room, GameClient session, IReadOnlyList<int> userIds);
    void RemoveAll(Room room, GameClient session);
    void RemoveOwn(Room room, GameClient session);
}

public interface IRoomRightsStore
{
    void Assign(uint roomId, int userId);
    void Remove(uint roomId, IReadOnlyList<int> userIds);
}

public sealed class RoomRightsStore(IDatabase database) : IRoomRightsStore
{
    public void Assign(uint roomId, int userId) => Execute((connection, transaction) =>
        connection.Execute("INSERT INTO room_rights (room_id, user_id) VALUES (@roomId, @userId)",
            new { roomId, userId }, transaction));

    public void Remove(uint roomId, IReadOnlyList<int> userIds) => Execute((connection, transaction) =>
    {
        foreach (var userId in userIds) {
            connection.Execute("DELETE FROM room_rights WHERE user_id=@userId AND room_id=@roomId LIMIT 1",
                new { userId, roomId }, transaction);
        }
    });

    private void Execute(Action<System.Data.IDbConnection, System.Data.IDbTransaction> mutation)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        mutation(connection, transaction);
        transaction.Commit();
    }
}

public sealed class RoomRightsService(IRoomRightsStore store, ILanguageManager languageManager, ICacheManager cacheManager) : IRoomRightsService
{
    public void Show(GameClient session)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;

        if (!habbo.InRoom || room == null || !room.CheckRights(session)) {
            return;
        }

        var holders = room.UsersWithRights.Select(cacheManager.GenerateUser)
            .Select(user => user == null
                ? new RoomRightHolder(0, "Unknown Error")
                : new RoomRightHolder(user.Id, user.Username))
            .ToArray();
        session.Send(new RoomRightsListComposer(room.Id, holders));
    }

    public void Assign(Room room, GameClient session, int userId)
    {
        if (!room.CheckRights(session, true)) {
            return;
        }

        if (room.UsersWithRights.Contains(userId)) {
            session.SendNotification(languageManager.TryGetValue("room.rights.user.has_rights"));

            return;
        }

        store.Assign(room.RoomId, userId);
        room.UsersWithRights.Add(userId);
        var roomUser = room.GetRoomUserManager().GetRoomUserByHabbo(userId);

        if (roomUser != null && !roomUser.IsBot) {
            roomUser.SetStatus("flatctrl 1");
            roomUser.UpdateNeeded = true;
            roomUser.GetClient()?.Send(new YouAreControllerComposer(1));
            var target = roomUser.GetClient()?.GetHabbo();

            if (target != null) {
                session.Send(new FlatControllerAddedComposer(room.RoomId, target.Id, target.Username));
            }

            return;
        }

        var user = cacheManager.GenerateUser(userId);

        if (user != null) {
            session.Send(new FlatControllerAddedComposer(room.RoomId, user.Id, user.Username));
        }
    }

    public void Remove(Room room, GameClient session, IReadOnlyList<int> userIds)
    {
        if (!room.CheckRights(session, true)) {
            return;
        }

        var removals = userIds.Where(id => id > 0 && room.UsersWithRights.Contains(id)).Distinct().ToArray();

        if (removals.Length == 0) {
            return;
        }

        store.Remove(room.Id, removals);

        foreach (var userId in removals) {
            PublishRemoval(room, userId, false);
            room.UsersWithRights.Remove(userId);
            session.Send(new FlatControllerRemovedComposer(room.Id, userId));
        }
    }

    public void RemoveAll(Room room, GameClient session)
    {
        if (!room.CheckRights(session, true)) {
            return;
        }

        var removals = room.UsersWithRights.ToArray();

        if (removals.Length == 0) {
            return;
        }

        store.Remove(room.Id, removals);

        foreach (var userId in removals) {
            PublishRemoval(room, userId, false);
            session.Send(new FlatControllerRemovedComposer(room.Id, userId));
            session.Send(new RoomRightsListComposer(room.Id, room.UsersWithRights.Select(id => new RoomRightHolder(id, cacheManager.GenerateUser(id)?.Username ?? "Unknown Error")).ToArray()));
            session.Send(new UserUpdateComposer(RoomUserStatusSnapshot.Capture(room.GetRoomUserManager().GetUserList())));
        }

        room.UsersWithRights.Clear();
    }

    public void RemoveOwn(Room room, GameClient session)
    {
        if (!room.CheckRights(session, false)) {
            return;
        }

        var userId = session.GetHabbo().Id;

        if (!room.UsersWithRights.Contains(userId)) {
            return;
        }

        store.Remove(room.Id, [userId]);
        PublishRemoval(room, userId, true);
        room.UsersWithRights.Remove(userId);
    }

    private static void PublishRemoval(Room room, int userId, bool own)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(userId);

        if (user == null || user.IsBot) {
            return;
        }

        user.RemoveStatus("flatctrl 1");
        user.UpdateNeeded = true;

        if (own) {
            user.GetClient()?.Send(new YouAreNotControllerComposer());
        }
        else {
            user.GetClient()?.Send(new YouAreControllerComposer(0));
        }
    }
}
