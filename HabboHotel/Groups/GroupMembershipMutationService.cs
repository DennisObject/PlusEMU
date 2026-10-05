using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Rooms.Permissions;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

public sealed record GroupMemberUpdateSnapshot(int GroupId, int Role, int UserId, string Username, string Look);

[Singleton]
public interface IGroupMemberIdentityLookup
{
    GroupMemberIdentity? Find(int userId);
}

public sealed record GroupMemberIdentity(int Id, string Username, string Look);

public sealed class GroupMemberIdentityLookup(IGameClientManager clients, ICacheManager cache) : IGroupMemberIdentityLookup
{
    public GroupMemberIdentity? Find(int userId)
    {
        var online = clients.GetClientByUserId(userId)?.GetHabbo();
        if (online != null)
            return new(online.Id, online.Username, online.Look);
        var cached = cache.GenerateUser(userId);
        return cached == null ? null : new(cached.Id, cached.Username, cached.Look);
    }
}

[Singleton]
public interface IGroupMembershipMutationService
{
    Task Accept(GameClient session, int groupId, int userId);
    Task Decline(GameClient session, int groupId, int userId);
    Task GiveAdmin(GameClient session, int groupId, int userId);
    Task TakeAdmin(GameClient session, int groupId, int userId);
}

public sealed class GroupMembershipMutationService(
    IGroupManager groups,
    IRoomManager rooms,
    IGroupMemberIdentityLookup identities,
    IGroupMembershipMutationStore store) : IGroupMembershipMutationService
{
    public Task Accept(GameClient session, int groupId, int userId)
    {
        if (!groups.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        var actor = session.GetHabbo();
        if ((actor.Id != group.CreatorId && !group.IsAdmin(actor.Id) && !actor.Access.Can(PermissionKeys.GroupAcceptAny)) ||
            !group.HasRequest(userId))
            return Task.CompletedTask;
        var identity = identities.Find(userId);
        if (!store.Accept(group.Id, userId))
            return Task.CompletedTask;

        group.HandleRequest(userId, true);
        SendMemberUpdate(session, group, userId, 2, identity);
        return Task.CompletedTask;
    }

    public Task GiveAdmin(GameClient session, int groupId, int userId) =>
        SetAdmin(session, groupId, userId, true);

    public Task Decline(GameClient session, int groupId, int userId)
    {
        if (!groups.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        var actor = session.GetHabbo();
        if ((actor.Id != group.CreatorId && !group.IsAdmin(actor.Id)) ||
            !group.HasRequest(userId) || !store.Decline(group.Id, userId))
            return Task.CompletedTask;
        group.HandleRequest(userId, false);
        session.Send(new UnknownGroupComposer(group.Id, userId));
        return Task.CompletedTask;
    }

    public Task TakeAdmin(GameClient session, int groupId, int userId) =>
        SetAdmin(session, groupId, userId, false);

    private Task SetAdmin(GameClient session, int groupId, int userId, bool isAdmin)
    {
        if (!groups.TryGetGroup(groupId, out var group))
            return Task.CompletedTask;
        var actor = session.GetHabbo();
        if (actor.Id != group.CreatorId || userId == group.CreatorId || !group.IsMember(userId))
            return Task.CompletedTask;
        var identity = identities.Find(userId);
        if (!store.SetAdmin(group.Id, userId, isAdmin))
            return Task.CompletedTask;

        if (isAdmin)
            group.MakeAdmin(userId);
        else
            group.TakeAdmin(userId);
        PublishController(group, userId, isAdmin);
        SendMemberUpdate(session, group, userId, isAdmin ? 1 : 2, identity);
        return Task.CompletedTask;
    }

    private void PublishController(Group group, int userId, bool isAdmin)
    {
        if (!rooms.TryGetRoom(group.RoomId, out var room))
            return;
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(userId);
        if (user == null)
            return;
        if (isAdmin)
        {
            if (!user.Statusses.ContainsKey("flatctrl 3"))
                user.SetStatus("flatctrl 3");
        }
        else if (user.Statusses.ContainsKey("flatctrl 3"))
            user.RemoveStatus("flatctrl 3");
        user.UpdateNeeded = true;
        user.GetClient()?.Send(new YouAreControllerComposer(isAdmin ? 3 : 0));
    }

    private static void SendMemberUpdate(
        GameClient session,
        Group group,
        int userId,
        int role,
        GroupMemberIdentity? identity)
    {
        if (identity == null)
        {
            session.Send(new UnknownGroupComposer(group.Id, userId));
            return;
        }
        session.Send(new GroupMemberUpdatedComposer(new(
            group.Id, role, identity.Id, identity.Username, identity.Look)));
    }
}
