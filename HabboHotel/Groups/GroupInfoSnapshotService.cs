using Dapper;
using Plus.Database;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Groups;

public sealed record GroupInfoSnapshot(
    int Id,
    GroupType Type,
    string Name,
    string Description,
    string Badge,
    uint RoomId,
    string RoomName,
    int MemberCount,
    string CreatedOn,
    string CreatorName,
    bool ViewerIsCreator,
    bool ViewerIsAdmin,
    bool ViewerIsMember,
    bool ViewerHasRequest,
    int PendingRequests,
    bool AdminOnlyDecoOpen,
    bool ForumEnabled);

public interface IGroupInfoSnapshotService
{
    GroupInfoSnapshot Capture(Group group, int viewerId);
}

public sealed class GroupInfoSnapshotService(IGameClientManager clientManager, ICacheManager cacheManager, IDatabase database) : IGroupInfoSnapshotService
{
    public GroupInfoSnapshot Capture(Group group, int viewerId)
    {
        var origin = (group.CreatedAt ?? DateTimeOffset.UnixEpoch).UtcDateTime;
        var room = group.GetRoom();
        var viewerIsCreator = group.CreatorId == viewerId;
        var viewerIsAdmin = group.IsAdmin(viewerId);
        return new GroupInfoSnapshot(
            group.Id,
            group.Type,
            group.Name,
            group.Description,
            group.Badge,
            group.RoomId,
            room != null ? room.Name : "No room found..",
            group.MemberCount,
            $"{origin.Day}-{origin.Month}-{origin.Year}",
            ResolveUsername(group.CreatorId),
            viewerIsCreator,
            viewerIsAdmin,
            group.IsMember(viewerId),
            group.HasRequest(viewerId),
            viewerIsCreator || viewerIsAdmin ? group.RequestCount : 0,
            group.AdminOnlyDeco == 0,
            group.ForumEnabled);
    }

    private string ResolveUsername(int userId)
    {
        var habbo = clientManager.GetClientByUserId(userId)?.GetHabbo();
        if (habbo != null)
            return habbo.Username;
        var user = cacheManager.GenerateUser(userId);
        if (user != null)
            return user.Username;
        using var connection = database.Connection();
        var name = connection.QuerySingleOrDefault<string>("SELECT username FROM users WHERE id=@userId LIMIT 1", new { userId });
        return string.IsNullOrEmpty(name) ? "Unknown User" : name;
    }
}
