using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class RemoveAllRightsEvent : RoomPacketEvent
{
    private readonly IRoomManager _roomManager;
    private readonly IDatabase _database;

    public RemoveAllRightsEvent(IRoomManager roomManager, IDatabase database)
    {
        _roomManager = roomManager;
        _database = database;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var instance = room;
        if (!instance.CheckRights(session, true))
            return Task.CompletedTask;
        foreach (var userId in new List<int>(instance.UsersWithRights))
        {
            var user = instance.GetRoomUserManager().GetRoomUserByHabbo(userId);
            if (user != null && !user.IsBot)
            {
                user.RemoveStatus("flatctrl 1");
                user.UpdateNeeded = true;
                user.GetClient().Send(new YouAreControllerComposer(0));
            }
            using (var dbClient = _database.GetQueryReactor())
            {
                dbClient.SetQuery("DELETE FROM `room_rights` WHERE `user_id` = @uid AND `room_id` = @rid LIMIT 1");
                dbClient.AddParameter("uid", userId);
                dbClient.AddParameter("rid", instance.Id);
                dbClient.RunQuery();
            }
            session.Send(new FlatControllerRemovedComposer(instance, userId));
            session.Send(new RoomRightsListComposer(instance.Id, instance.UsersWithRights
                .Select(id => PlusEnvironment.Game.CacheManager.GenerateUser(id))
                .Select(user => user == null
                    ? new RoomRightHolder(0, "Unknown Error")
                    : new RoomRightHolder(user.Id, user.Username))
                .ToArray()));
            session.Send(new UserUpdateComposer(instance.GetRoomUserManager().GetUserList().ToList()));
        }
        if (instance.UsersWithRights.Count > 0)
            instance.UsersWithRights.Clear();
        return Task.CompletedTask;
    }
}
