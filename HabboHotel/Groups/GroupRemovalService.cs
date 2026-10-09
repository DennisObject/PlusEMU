using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

[Singleton]
public interface IGroupRemovalService
{
    Task Delete(GameClient session, int groupId);
    Task ConfirmRemove(GameClient session, int groupId, int userId);
    Task Remove(GameClient session, int groupId, int userId);
}

public sealed class GroupRemovalService(
    IGroupManager _groupManager,
    IRoomManager _roomManager,
    ISettingsManager _settingsManager,
    IGameClientManager _clients,
    IGroupInfoSnapshotService _groupInfo,
    IGroupRemovalStore _store,
    IAccountSessionGate _sessions) : IGroupRemovalService
{
    public Task Delete(GameClient session, int groupId)
    {
        if (!_groupManager.TryGetGroup(groupId, out var group)) {
            session.SendNotification("Oops, we couldn't find that group!");

            return Task.CompletedTask;
        }

        if (group.CreatorId != session.GetHabbo().Id && !session.GetHabbo().Access.Can(PermissionKeys.GroupDeleteOverride)) {
            session.SendNotification("Oops, only the group owner can delete a group!");

            return Task.CompletedTask;
        }

        List<int> memberIds;

        lock (group) {
            if (!_groupManager.TryGetGroup(group.Id, out var current) || !ReferenceEquals(current, group)) {
                return Task.CompletedTask;
            }

            if (group.MemberCount >= Convert.ToInt32(_settingsManager.TryGetValue("group.delete.member.limit")) &&
                !session.GetHabbo().Access.Can(PermissionKeys.GroupDeleteLimitOverride)) {
                session.SendNotification(
                    $"Oops, your group exceeds the maximum amount of members ({Convert.ToInt32(_settingsManager.TryGetValue("group.delete.member.limit"))}) a group can exceed before being eligible for deletion. Seek assistance from a staff member.");

                return Task.CompletedTask;
            }

            memberIds = group.GetAllMembers.Append(session.GetHabbo().Id).Distinct().ToList();

            if (!_store.Delete(group.Id)) {
                return Task.CompletedTask;
            }

            _roomManager.TryGetRoom(group.RoomId, out var room);

            if (room != null) {
                room.Group = null;
            }

            _groupManager.DeleteGroup(group.Id);

            if (room != null) {
                _roomManager.UnloadRoom(room.Id);
            }
        }

        foreach (var memberId in memberIds) {
            using var lease = _sessions.Enter(memberId);
            var client = _clients.GetClientByUserId(memberId);

            if (client == null) {
                continue;
            }

            var stats = client.GetHabbo().HabboStats;

            if (stats != null && stats.FavouriteGroupId == group.Id) {
                stats.FavouriteGroupId = 0;
            }

            client.Send(new GroupDeactivatedComposer(group.Id));
        }

        session.SendNotification("You have successfully deleted your group.");

        return Task.CompletedTask;
    }

    public Task ConfirmRemove(GameClient session, int groupId, int userId)
    {
        if (!_groupManager.TryGetGroup(groupId, out var group)) {
            return Task.CompletedTask;
        }

        if (userId == group.CreatorId) {
            return Task.CompletedTask;
        }

        var actorId = session.GetHabbo().Id;

        // leaving is the member's own to ask about (Remove allows it too); removing someone else takes the owner, or an admin for a non admin
        if (actorId != userId) {
            if (actorId != group.CreatorId && !group.IsAdmin(actorId)) {
                return Task.CompletedTask;
            }

            if (group.IsAdmin(userId) && actorId != group.CreatorId) {
                return Task.CompletedTask;
            }
        }

        if (!group.IsMember(userId)) {
            return Task.CompletedTask;
        }

        var furnitureCount = _store.CountFurniture(userId, group.RoomId);
        session.Send(new GroupConfirmRemoveMemberComposer(userId, furnitureCount));

        return Task.CompletedTask;
    }

    public Task Remove(GameClient session, int groupId, int userId)
    {
        if (!_groupManager.TryGetGroup(groupId, out var group)) {
            return Task.CompletedTask;
        }

        using var lease = _sessions.Enter(userId);

        lock (group) {
            if (!_groupManager.TryGetGroup(group.Id, out var current) || !ReferenceEquals(current, group)) {
                return Task.CompletedTask;
            }

            if (userId == group.CreatorId) {
                return Task.CompletedTask;
            }

            if (userId == session.GetHabbo().Id) {
                // Leaving also clears a favourite, which needs the loaded statistics to know about it.
                if (session.GetHabbo().HabboStats is not { } stats) {
                    return Task.CompletedTask;
                }

                var wasAdmin = group.IsAdmin(userId);

                if (!_store.RemoveMember(group.Id, userId, group.IsMember(userId),
                        stats.FavouriteGroupId == groupId)) {
                    return Task.CompletedTask;
                }

                if (group.IsMember(userId)) {
                    group.DeleteMember(userId);
                }

                if (wasAdmin && _roomManager.TryGetRoom(group.RoomId, out var adminRoom)) {
                    var user = adminRoom.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

                    if (user != null) {
                        user.RemoveStatus("flatctrl 1");
                        user.UpdateNeeded = true;

                        user.GetClient()?.Send(new YouAreControllerComposer(0));
                    }
                }

                session.Send(new GroupInfoComposer(_groupInfo.Capture(group, session.GetHabbo().Id)));

                if (stats.FavouriteGroupId == groupId) {
                    stats.FavouriteGroupId = 0;

                    if (group.AdminOnlyDeco == 0 && _roomManager.TryGetRoom(group.RoomId, out var room)) {
                        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

                        if (user != null) {
                            user.RemoveStatus("flatctrl 1");
                            user.UpdateNeeded = true;

                            user.GetClient()?.Send(new YouAreControllerComposer(0));
                        }
                    }

                    if (session.GetHabbo().InRoom && session.GetHabbo().CurrentRoom is { } currentRoom) {
                        var user = currentRoom.GetRoomUserManager()
                            .GetRoomUserByHabbo(session.GetHabbo().Id);

                        if (user != null) {
                            currentRoom
                                .SendPacket(new UpdateFavouriteGroupComposer(FavouriteGroupSnapshot.Capture(group, user.VirtualId)));
                        }

                        currentRoom
                            .SendPacket(new RefreshFavouriteGroupComposer(session.GetHabbo().Id));
                    }
                    else {
                        session.Send(new RefreshFavouriteGroupComposer(session.GetHabbo().Id));
                    }
                }

                return Task.CompletedTask;
            }

            if (group.CreatorId == session.GetHabbo().Id || group.IsAdmin(session.GetHabbo().Id)) {
                if (!group.IsMember(userId)) {
                    return Task.CompletedTask;
                }

                if (group.IsAdmin(userId) && group.CreatorId != session.GetHabbo().Id) {
                    session.SendNotification(
                        "Sorry, only group creators can remove other administrators from the group.");

                    return Task.CompletedTask;
                }

                if (!_store.RemoveMember(group.Id, userId, true, true)) {
                    return Task.CompletedTask;
                }

                if (group.IsMember(userId)) {
                    group.DeleteMember(userId);
                }

                if (_roomManager.TryGetRoom(group.RoomId, out var room)) {
                    var user = room.GetRoomUserManager().GetRoomUserByHabbo(userId);

                    if (user != null) {
                        user.RemoveStatus("flatctrl 1");
                        user.RemoveStatus("flatctrl 3");
                        user.UpdateNeeded = true;
                        user.GetClient()?.Send(new YouAreControllerComposer(0));
                    }
                }

                var removedClient = _clients.GetClientByUserId(userId);

                if (removedClient != null) {
                    var habbo = removedClient.GetHabbo();
                    var stats = habbo.HabboStats;

                    if (stats != null && stats.FavouriteGroupId == groupId) {
                        stats.FavouriteGroupId = 0;

                        if (habbo.CurrentRoom != null) {
                            var favouriteUser = habbo.CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(userId);

                            if (favouriteUser != null) {
                                habbo.CurrentRoom.SendPacket(new UpdateFavouriteGroupComposer(FavouriteGroupSnapshot.Capture(null, favouriteUser.VirtualId)));
                            }

                            habbo.CurrentRoom.SendPacket(new RefreshFavouriteGroupComposer(userId));
                        }
                        else {
                            removedClient.Send(new RefreshFavouriteGroupComposer(userId));
                        }
                    }

                    removedClient.Send(new GroupInfoComposer(_groupInfo.Capture(group, removedClient.GetHabbo().Id)));
                }

                session.Send(new UnknownGroupComposer(group.Id, userId));
            }

            return Task.CompletedTask;
        }
    }
}
