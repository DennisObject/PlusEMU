using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

public readonly record struct GroupSettingsRequest(int GroupId, int Type, int FurniOptions, bool ForumEnabled);

[Singleton]
public interface IGroupSettingsService
{
    Task Update(GameClient session, GroupSettingsRequest request);
}

public sealed class GroupSettingsService(
    IGroupManager groups,
    IRoomManager rooms,
    IGroupInfoSnapshotService groupInfo,
    IGroupSettingsStore store) : IGroupSettingsService
{
    public Task Update(GameClient session, GroupSettingsRequest request)
    {
        if (!groups.TryGetGroup(request.GroupId, out var group))
            return Task.CompletedTask;
        lock (group)
        {
            if (!groups.TryGetGroup(group.Id, out var current) || !ReferenceEquals(current, group) ||
                group.CreatorId != session.GetHabbo().Id)
                return Task.CompletedTask;
            var type = request.Type switch
            {
                1 => GroupType.Locked,
                2 => GroupType.Private,
                _ => GroupType.Open
            };
            var requests = type == GroupType.Locked
                ? ImmutableArray<int>.Empty
                : group.GetRequests.ToImmutableArray();
            if (!store.Update(group.Id, type, request.FurniOptions == 1, request.ForumEnabled, requests))
                return Task.CompletedTask;

            group.Type = type;
            foreach (var userId in requests)
                group.HandleRequest(userId, false);
            group.AdminOnlyDeco = request.FurniOptions;
            group.ForumEnabled = request.ForumEnabled;
            group.HasForum = request.ForumEnabled;
            PublishRoomRights(group, request.FurniOptions);
            session.Send(new GroupInfoComposer(groupInfo.Capture(group, session.GetHabbo().Id)));
            return Task.CompletedTask;
        }
    }

    private void PublishRoomRights(Group group, int furniOptions)
    {
        if (!rooms.TryGetRoom(group.RoomId, out var room))
            return;
        foreach (var user in room.GetRoomUserManager().GetRoomUsers().ToList())
        {
            if (room.OwnerId == user.UserId || group.IsAdmin(user.UserId) || !group.IsMember(user.UserId))
                continue;
            if (furniOptions == 1)
            {
                user.RemoveStatus("flatctrl 1");
                user.UpdateNeeded = true;
                user.GetClient()?.Send(new YouAreControllerComposer(0));
            }
            else if (furniOptions == 0 && !user.Statusses.ContainsKey("flatctrl 1"))
            {
                user.SetStatus("flatctrl 1");
                user.UpdateNeeded = true;
                user.GetClient()?.Send(new YouAreControllerComposer(1));
            }
        }
    }
}
