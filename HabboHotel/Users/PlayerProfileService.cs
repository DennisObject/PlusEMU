using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users.UserData;

namespace Plus.HabboHotel.Users;

public sealed record ProfileGroupSnapshot(int Id, string Name, string Badge, string FirstColor, string SecondColor, bool Favorite, bool ForumEnabled);
public sealed record PlayerProfileSnapshot(int Id, string Username, string Look, string Motto, DateTimeOffset? CreatedAt,
    int AchievementPoints, int FriendCount, bool IsFriend, bool RequestedFriendship, bool Online,
    ImmutableArray<ProfileGroupSnapshot> Groups, int LastOnlineSeconds);

public interface IPlayerProfileService
{
    Task Open(GameClient session, int userId);
}

public sealed class PlayerProfileService(IGroupManager groupManager, IMessengerDataLoader messengerDataLoader,
    IGameClientManager clients, IUserDataFactory users, IHabboStatsService statsLoader, TimeProvider clock) : IPlayerProfileService
{
    public async Task Open(GameClient session, int userId)
    {
        var target = clients.GetClientByUserId(userId)?.GetHabbo() ?? await users.GetUserDataByIdAsync(userId);

        if (target == null)
        {
            session.SendNotification("An error occurred whilst finding that user's profile.");

            return;
        }

        var stats = await statsLoader.LoadHabboStats(userId);
        target.HabboStats = stats;
        var groups = groupManager.GetGroupsForUser(target.Id).Select(group => new ProfileGroupSnapshot(group.Id, group.Name,
            group.Badge, groupManager.GetColourCode(group.Colour1, true), groupManager.GetColourCode(group.Colour2, false),
            stats.FavouriteGroupId == group.Id, group.ForumEnabled)).ToImmutableArray();
        var friendCount = await messengerDataLoader.GetFriendCount(userId);
        var viewer = session.GetHabbo();
        var otherUser = target.Id != viewer.Id;
        var friendship = otherUser && viewer.Messenger.FriendshipExists(target.Id);
        var now = clock.GetUtcNow();
        var elapsed = (int)Math.Clamp(target.LastOnlineAt is { } lastOnline ? (now - lastOnline).Ticks / TimeSpan.TicksPerSecond : 0, 0, int.MaxValue);
        session.Send(new ProfileInformationComposer(new(target.Id, target.Username, target.Look, target.Motto,
            target.AccountCreatedAt, stats.AchievementPoints, friendCount, friendship,
            otherUser && !friendship && viewer.Messenger.OutstandingFriendRequests.Contains(target.Id),
            clients.GetClientByUserId(target.Id) != null, groups, elapsed)));
    }
}
