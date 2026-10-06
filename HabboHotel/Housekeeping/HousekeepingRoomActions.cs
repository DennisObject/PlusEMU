using Dapper;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Settings;
using Plus.Database;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using static Plus.HabboHotel.Housekeeping.HousekeepingErrors;

namespace Plus.HabboHotel.Housekeeping;

public interface IHousekeepingRoomActions
{
    HousekeepingOutcome SetState(Habbo actor, int roomId, bool open);
    HousekeepingOutcome Mute(Habbo actor, int roomId, int minutes);
    HousekeepingOutcome KickAll(Habbo actor, int roomId);
    HousekeepingOutcome TransferOwnership(Habbo actor, int roomId, int newOwnerId);
    HousekeepingOutcome Delete(Habbo actor, int roomId);
}

public sealed class HousekeepingRoomActions : IHousekeepingRoomActions
{
    private readonly IRoomManager _roomManager;
    private readonly IRoomDeletionService _roomDeletion;
    private readonly IHousekeepingUserStore _users;
    private readonly IDatabase _database;
    private readonly IAccessControl _permissions;

    public HousekeepingRoomActions(IRoomManager roomManager, IRoomDeletionService roomDeletion, IHousekeepingUserStore users, IDatabase database, IAccessControl permissions)
    {
        _roomManager = roomManager;
        _roomDeletion = roomDeletion;
        _users = users;
        _database = database;
        _permissions = permissions;
    }

    public HousekeepingOutcome SetState(Habbo actor, int roomId, bool open)
    {
        if (Load(actor, roomId, out var room) is { } denied) {
            return denied;
        }

        room.Access = open ? RoomAccess.Open : RoomAccess.Doorbell;

        using (var connection = _database.Connection()) {
            connection.Execute("UPDATE `rooms` SET `state` = @state WHERE `id` = @roomId LIMIT 1", new { state = open ? "open" : "locked", roomId });
        }

        room.SendPacket(new RoomSettingsSavedComposer(room.RoomId));
        room.SendPacket(new RoomInfoUpdatedComposer(room.RoomId));

        return HousekeepingOutcome.Success(Label(room), $"open={open}");
    }

    /// <summary>Plus room mutes are an in-memory toggle without expiry: minutes above zero mute, zero unmutes.</summary>
    public HousekeepingOutcome Mute(Habbo actor, int roomId, int minutes)
    {
        if (!HousekeepingLimits.InRange(minutes, 0, HousekeepingLimits.MaxRoomMuteMinutes)) {
            return Invalid(roomId);
        }

        if (Loaded(actor, roomId, out var room) is { } denied) {
            return denied;
        }

        room.RoomMuted = minutes > 0;
        room.SendPacket(new RoomMuteSettingsComposer(room.RoomMuted));

        return HousekeepingOutcome.Success(Label(room), $"minutes={minutes} muted={room.RoomMuted}");
    }

    public HousekeepingOutcome KickAll(Habbo actor, int roomId)
    {
        if (Loaded(actor, roomId, out var room) is { } denied) {
            return denied;
        }

        var kicked = 0;

        foreach (var roomUser in room.GetRoomUserManager().GetUserList().ToList()) {
            var habbo = roomUser?.GetClient()?.GetHabbo();

            if (roomUser == null || roomUser.IsBot || habbo == null || habbo.Id == actor.Id || !actor.Access.Outranks(habbo.Access)) {
                continue;
            }

            room.GetRoomUserManager().RemoveUserFromRoom(roomUser.GetClient(), true);
            kicked++;
        }

        return HousekeepingOutcome.Success(Label(room), $"kicked={kicked}");
    }

    public HousekeepingOutcome TransferOwnership(Habbo actor, int roomId, int newOwnerId)
    {
        if (newOwnerId <= 0) {
            return Invalid(roomId);
        }

        if (Load(actor, roomId, out var room) is { } denied) {
            return denied;
        }

        var newOwner = _users.Find(newOwnerId);

        if (newOwner == null) {
            return HousekeepingOutcome.Fail(NewOwnerNotFound, Label(room), $"newOwnerId={newOwnerId}");
        }

        if (newOwner.Id == actor.Id || !_permissions.Outranks(actor.Id, newOwner.Id)) {
            return HousekeepingOutcome.Fail(RankTooHigh, Label(room), $"newOwnerId={newOwner.Id}");
        }

        // A group home room belongs to the group; moving it would split group and room ownership.
        if (room.Group != null) {
            return HousekeepingOutcome.Fail(RoomActionFailed, Label(room), "group_room");
        }

        using (var connection = _database.Connection()) {
            connection.Execute("UPDATE `rooms` SET `owner` = @newOwnerId WHERE `id` = @roomId LIMIT 1", new { newOwnerId, roomId });
        }

        var previousOwner = room.OwnerName;
        room.OwnerId = newOwner.Id;
        room.OwnerName = newOwner.Username;
        room.SendPacket(new RoomInfoUpdatedComposer(room.RoomId));

        return HousekeepingOutcome.Success(Label(room), $"from={previousOwner} to={newOwner.Username}");
    }

    public HousekeepingOutcome Delete(Habbo actor, int roomId)
    {
        if (Load(actor, roomId, out var room) is { } denied) {
            return denied;
        }

        var target = Label(room);
        _roomDeletion.Delete(room);

        return HousekeepingOutcome.Success(target, $"owner={room.OwnerName}");
    }

    private HousekeepingOutcome? Load(Habbo actor, int roomId, out Room room)
    {
        room = null!;

        if (roomId <= 0) {
            return Invalid(roomId);
        }

        if (!_roomManager.TryLoadRoom((uint)roomId, out var loaded)) {
            return HousekeepingOutcome.Fail(RoomNotFound, HousekeepingTarget.Room(roomId));
        }

        return Guard(actor, room = loaded);
    }

    private HousekeepingOutcome? Loaded(Habbo actor, int roomId, out Room room)
    {
        room = null!;

        if (roomId <= 0) {
            return Invalid(roomId);
        }

        if (!_roomManager.TryGetRoom((uint)roomId, out var loaded)) {
            return HousekeepingOutcome.Fail(RoomNotLoaded, HousekeepingTarget.Room(roomId));
        }

        return Guard(actor, room = loaded);
    }

    private HousekeepingOutcome? Guard(Habbo actor, Room room)
    {
        return room.OwnerId == actor.Id || _permissions.Outranks(actor.Id, room.OwnerId)
            ? null : HousekeepingOutcome.Fail(RankTooHigh, Label(room), $"ownerId={room.OwnerId}");
    }

    private static HousekeepingOutcome Invalid(int roomId) => HousekeepingOutcome.Fail(InvalidInput, HousekeepingTarget.Room(Math.Max(roomId, 0)));

    private static HousekeepingTarget Label(Room room) => HousekeepingTarget.Room((int)room.Id, room.Name ?? string.Empty);
}
