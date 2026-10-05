using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Groups;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups;

[Singleton]
public interface IGroupParticipationService
{
    Task Join(GameClient session, int groupId);
    Task SetFavourite(GameClient session, int groupId);
    Task RemoveFavourite(GameClient session);
}

// Lock order is account gate, then group lock, then the database rows. Each operation holds the gate until its packets are sent,
// so two requests from one account publish in the same order their writes commit.
public sealed class GroupParticipationService(IGroupManager groups, IGroupInfoSnapshotService groupInfo, IGameClientManager clients,
    IGroupParticipationStore store, IAccountSessionGate accounts) : IGroupParticipationService
{
    private const int MaxGroupsPerUser = 1500;
    private const string LimitMessage = "Oops, it appears that you've hit the group membership limit! You can only join upto 1,500 groups.";

    public Task Join(GameClient session, int groupId)
    {
        var habbo = session.GetHabbo();
        using var account = accounts.Enter(habbo.Id);
        if (!groups.TryGetGroup(groupId, out var found))
            return Task.CompletedTask;
        lock (found)
        {
            // Settings and deletion publish under the same group lock, so this check and the snapshots below see one state.
            if (!IsCurrent(found))
                return Task.CompletedTask;
            var group = found;
            if (group.IsMember(habbo.Id) || group.IsAdmin(habbo.Id) || group.HasRequest(habbo.Id) && group.Type == GroupType.Private)
                return Task.CompletedTask;
            if (groups.GetGroupsForUser(habbo.Id).Count >= MaxGroupsPerUser)
            {
                session.Send(new BroadcastMessageAlertComposer(LimitMessage));
                return Task.CompletedTask;
            }

            // The store repeats the limit under the account row lock, so concurrent joins across groups cannot exceed it.
            var outcome = store.Join(habbo.Id, group.Id, group.Type == GroupType.Locked, MaxGroupsPerUser);
            if (outcome == GroupJoinOutcome.LimitReached)
            {
                session.Send(new BroadcastMessageAlertComposer(LimitMessage));
                return Task.CompletedTask;
            }
            if (outcome == GroupJoinOutcome.Refused)
                return Task.CompletedTask;

            group.PublishJoin(habbo.Id);
            if (group.Type == GroupType.Locked)
                PublishRequest(session, group, habbo);
            else
                PublishMembership(session, group, habbo);
        }
        return Task.CompletedTask;
    }

    public Task SetFavourite(GameClient session, int groupId)
    {
        if (groupId == 0)
            return Task.CompletedTask;
        var habbo = session.GetHabbo();
        using var account = accounts.Enter(habbo.Id);
        if (!groups.TryGetGroup(groupId, out var found))
            return Task.CompletedTask;
        lock (found)
        {
            if (!IsCurrent(found) || !store.SaveFavourite(habbo.Id, found.Id))
                return Task.CompletedTask;

            habbo.HabboStats.FavouriteGroupId = found.Id;
            if (habbo.InRoom && habbo.CurrentRoom is { } room)
            {
                room.SendPacket(new RefreshFavouriteGroupComposer(habbo.Id));
                room.SendPacket(new HabboGroupBadgesComposer([new(found.Id, found.Badge)]));
                var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
                if (user != null)
                    room.SendPacket(new UpdateFavouriteGroupComposer(FavouriteGroupSnapshot.Capture(found, user.VirtualId)));
            }
            else
                session.Send(new RefreshFavouriteGroupComposer(habbo.Id));
        }
        return Task.CompletedTask;
    }

    public Task RemoveFavourite(GameClient session)
    {
        var habbo = session.GetHabbo();
        using var account = accounts.Enter(habbo.Id);
        if (!store.SaveFavourite(habbo.Id, 0))
            return Task.CompletedTask;

        habbo.HabboStats.FavouriteGroupId = 0;
        if (habbo.InRoom && habbo.CurrentRoom is { } room)
        {
            var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
            if (user != null)
                room.SendPacket(new UpdateFavouriteGroupComposer(FavouriteGroupSnapshot.Capture(null, user.VirtualId)));
            room.SendPacket(new RefreshFavouriteGroupComposer(habbo.Id));
        }
        else
            session.Send(new RefreshFavouriteGroupComposer(habbo.Id));
        return Task.CompletedTask;
    }

    // A group deleted or replaced while this request waited for the lock must not be published into.
    private bool IsCurrent(Group group) => groups.TryGetGroup(group.Id, out var current) && ReferenceEquals(current, group);

    private void PublishRequest(GameClient session, Group group, Habbo requester)
    {
        var admins = clients.GetClients.ToList().Where(client => client != null && client.GetHabbo() != null && group.IsAdmin(client.GetHabbo().Id)).ToList();
        var requested = new GroupMemberUpdateSnapshot(group.Id, 3, requester.Id, requester.Username, requester.Look);
        foreach (var client in admins) client.Send(new GroupMembershipRequestedComposer(requested));
        session.Send(new GroupInfoComposer(groupInfo.Capture(group, requester.Id)));
    }

    private void PublishMembership(GameClient session, Group group, Habbo habbo)
    {
        session.Send(new GroupFurniConfigComposer(groups.GetGroupsForUser(habbo.Id)
            .Select(memberGroup => new GroupFurniConfig(
                memberGroup.Id,
                memberGroup.Name,
                memberGroup.Badge,
                groups.GetColourCode(memberGroup.Colour1, true),
                groups.GetColourCode(memberGroup.Colour2, false),
                memberGroup.CreatorId,
                memberGroup.ForumEnabled))
            .ToArray()));
        session.Send(new GroupInfoComposer(groupInfo.Capture(group, habbo.Id)));
        if (habbo.CurrentRoom != null)
            habbo.CurrentRoom.SendPacket(new RefreshFavouriteGroupComposer(habbo.Id));
        else
            session.Send(new RefreshFavouriteGroupComposer(habbo.Id));
    }
}
