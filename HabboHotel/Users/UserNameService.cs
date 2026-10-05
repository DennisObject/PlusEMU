using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users.UserData;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Users;

[Singleton]
public interface IUserNameService
{
    Task Check(GameClient session, string name);
    Task Change(GameClient session, string name);
}

public sealed class UserNameService(
    IUserDataFactory users,
    IWordFilterManager wordFilter,
    IGameClientManager clients,
    IRoomManager rooms,
    IAchievementManager achievements,
    INameChangeStore store,
    TimeProvider clock,
    ILogger<UserNameService> logger) : IUserNameService
{
    private const string AllowedCharacters = "abcdefghijklmnopqrstuvwxyz.,_-;:?!1234567890";

    public async Task Check(GameClient session, string name)
    {
        var habbo = session.GetHabbo();
        var inUse = await users.HabboExists(name);
        var lowerName = name.ToLower();
        if (lowerName.Any(character => !AllowedCharacters.Contains(character)) ||
            wordFilter.IsFiltered(name) || HasReservedCheckPrefix(habbo, lowerName))
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.InvalidName));
            return;
        }
        if (name.Length > 15)
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.TooLong));
            return;
        }
        if (name.Length < 3)
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.TooShort));
            return;
        }
        if (inUse)
        {
            session.Send(new NameChangeUpdateComposer(name, NameChangeError.InUse, ["100", "101", "102"]));
            return;
        }
        session.Send(new NameChangeUpdateComposer(name, NameChangeError.None));
    }

    public async Task Change(GameClient session, string newName)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;
        if (room == null)
            return;
        var roomUser = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Username);
        if (roomUser == null)
            return;

        var oldName = habbo.Username;
        var changedAt = clock.GetUtcNow();
        if (newName == oldName)
        {
            if (!Persist(habbo.Id, oldName, newName, changedAt, false))
                return;
            habbo.LastNameChangedAt = changedAt;
            session.Send(new UpdateUsernameComposer(newName));
            return;
        }
        if (!NameChangePolicy.CanChange(habbo, changedAt))
        {
            session.SendNotification("Oops, it appears you currently cannot change your username!");
            return;
        }
        if (await users.HabboExists(newName))
            return;

        var lowerName = newName.ToLower();
        if (lowerName.Any(character => !AllowedCharacters.Contains(character)) ||
            HasReservedChangePrefix(habbo, lowerName) || newName.Length is > 15 or < 3)
            return;

        if (!ReferenceEquals(habbo.CurrentRoom, room) ||
            room.GetRoomUserManager().GetRoomUserByHabbo(oldName) != roomUser)
            return;
        if (!clients.UpdateClientUsername(session, oldName, newName))
        {
            session.SendNotification("Oops! An issue occoured whilst updating your username.");
            return;
        }
        if (!Persist(habbo.Id, oldName, newName, changedAt, true))
        {
            if (!clients.UpdateClientUsername(session, newName, oldName))
                logger.LogCritical("Failed to roll back client username reservation for user {UserId}", habbo.Id);
            return;
        }

        habbo.ChangingName = false;
        room.GetRoomUserManager().RemoveUserFromRoom(session, true);
        habbo.Username = newName;
        habbo.LastNameChangedAt = changedAt;
        habbo.Messenger.NotifyChangesToFriends();
        session.Send(new UpdateUsernameComposer(newName));
        room.SendPacket(new UserNameChangeComposer(room.Id, roomUser.VirtualId, newName));
        foreach (var ownedRoom in rooms.GetRooms().ToList())
        {
            if (ownedRoom == null || ownedRoom.OwnerId != habbo.Id || ownedRoom.OwnerName == newName)
                continue;
            ownedRoom.OwnerName = newName;
            ownedRoom.SendPacket(new RoomInfoUpdatedComposer(ownedRoom.Id));
        }
        achievements.ProgressAchievement(session, "ACH_Name", 1);
        session.Send(new RoomForwardComposer(room.Id));
    }

    private bool Persist(int userId, string oldName, string newName, DateTimeOffset changedAt, bool writeLog)
    {
        try
        {
            return store.Change(userId, oldName, newName, changedAt, writeLog);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to persist username change for user {UserId}", userId);
            return false;
        }
    }

    private static bool HasReservedCheckPrefix(Habbo habbo, string lowerName) =>
        !habbo.Access.Can(PermissionKeys.ModerationTool) && lowerName.Contains("mod") ||
        lowerName.Contains("adm") || lowerName.Contains("admin") || lowerName.Contains("m0d") ||
        !lowerName.Contains("mod") && habbo.Access.Can(PermissionKeys.AvatarNameStaffPrefixRequired);

    private static bool HasReservedChangePrefix(Habbo habbo, string lowerName) =>
        !habbo.Access.Can(PermissionKeys.ModerationTool) && lowerName.Contains("mod") ||
        lowerName.Contains("adm") || lowerName.Contains("admin") || lowerName.Contains("m0d") ||
        lowerName.Contains("mob") || lowerName.Contains("m0b") ||
        !lowerName.Contains("mod") && habbo.Access.Can(PermissionKeys.AvatarNameStaffPrefixRequired);
}
