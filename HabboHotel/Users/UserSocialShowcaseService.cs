using System.Collections.Concurrent;
using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users.Ignores;
using Plus.HabboHotel.Users.Messenger;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Users;

public sealed record RelationshipEntry(int Type, int Count, int UserId, string Username, string Look);
public sealed record RelationshipSnapshot(int UserId, ImmutableArray<RelationshipEntry> Entries);
public sealed record GroupBadgeSnapshot(int GroupId, string Badge);

[Singleton]
public interface IUserSocialShowcaseService
{
    Task ShowRelationships(GameClient session, int userId);
    Task ShowIgnoredUsers(GameClient session);
    Task ShowGroupBadges(GameClient session);
}

public sealed class UserSocialShowcaseService(
    IMessengerDataLoader messengerData,
    IGameClientManager clients,
    IIgnoredUsersService ignoredUsers,
    IGroupManager groups) : IUserSocialShowcaseService
{
    public async Task ShowRelationships(GameClient session, int userId)
    {
        var target = clients.GetClientByUserId(userId)?.GetHabbo();
        Dictionary<int, (MessengerBuddy buddy, int count)> relationships = target == null
            ? await messengerData.GetRelationshipsForUserAsync(userId)
            : HabboMessenger.GetRelationships(new ConcurrentDictionary<int, MessengerBuddy>(target.Messenger.Friends));
        var entries = relationships.Select(pair => new RelationshipEntry(
            pair.Key, pair.Value.count, pair.Value.buddy.Id, pair.Value.buddy.Username, pair.Value.buddy.Look))
            .ToImmutableArray();
        session.Send(new GetRelationshipsComposer(new(userId, entries)));
    }

    public async Task ShowIgnoredUsers(GameClient session)
    {
        var ignored = session.GetHabbo().IgnoresComponent.IgnoredUsers.ToArray();
        var names = await ignoredUsers.GetIgnoredUsersByName(ignored);
        session.Send(new IgnoredUsersComposer(names));
    }

    public Task ShowGroupBadges(GameClient session)
    {
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        var badges = groups.GetAllBadgesInRoom(room);
        if (badges == null)
            return Task.CompletedTask;
        var snapshot = badges.Select(pair => new GroupBadgeSnapshot(pair.Key, pair.Value)).ToImmutableArray();
        room.SendPacket(new HabboGroupBadgesComposer(snapshot));
        session.Send(new HabboGroupBadgesComposer(snapshot));
        return Task.CompletedTask;
    }
}
