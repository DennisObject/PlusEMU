using System.Globalization;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms.AI;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

public sealed record RoomUserSnapshot(int Id, string Name, string Motto, string Look, int VirtualId, int X, int Y,
    string Z, int Rotation, int Kind, string Gender, int GroupId, string GroupName, int AchievementPoints,
    bool IsPet, int PetType, int OwnerId, string OwnerName, bool HasSaddle, bool IsRidden);

[Scoped]
public interface IRoomUserSnapshotService
{
    RoomUserSnapshot? Capture(RoomUser user);
    IReadOnlyList<RoomUserSnapshot> Capture(IEnumerable<RoomUser> users);
}

public sealed class RoomUserSnapshotService(IGroupManager groups, IGameClientManager clients, ICacheManager cache,
    IDatabase database) : IRoomUserSnapshotService
{
    public IReadOnlyList<RoomUserSnapshot> Capture(IEnumerable<RoomUser> users) =>
        users.Select(Capture).Where(snapshot => snapshot != null).Select(snapshot => snapshot!).ToArray();

    public RoomUserSnapshot? Capture(RoomUser user)
    {
        if (!user.IsBot) {
            var habbo = clients.GetClientByUserId(user.HabboId)?.GetHabbo();

            if (habbo == null) {
                return null;
            }

            Group? group = null;
            var favouriteGroupId = habbo.HabboStats?.FavouriteGroupId ?? 0;

            if (favouriteGroupId > 0) {
                groups.TryGetGroup(favouriteGroupId, out group);
            }

            return new(habbo.Id, habbo.Username, habbo.Motto, habbo.Look, user.VirtualId, user.X, user.Y,
                user.Z.ToString(CultureInfo.InvariantCulture), user.RotBody, 1, habbo.Gender.ToLowerInvariant(),
                group?.Id ?? 0, group?.Name ?? "", habbo.HabboStats?.AchievementPoints ?? 0,
                false, 0, 0, "", false, false);
        }

        var kind = user.BotData.AiType == BotAiType.Pet ? 2 : 4;

        if (user.IsPet) {
            return new(user.BotAi.BaseId, user.BotData.Name, user.BotData.Motto, user.PetData.Look.ToLowerInvariant(),
                user.VirtualId, user.X, user.Y, user.Z.ToString(CultureInfo.InvariantCulture), 0, kind, "", 0, "", 0,
                true, user.PetData.Type, user.PetData.OwnerId, user.PetData.OwnerName, user.PetData.Saddle > 0,
                user.RidingHorse);
        }

        return new(user.BotAi.BaseId, user.BotData.Name, user.BotData.Motto, user.BotData.Look.ToLowerInvariant(),
            user.VirtualId, user.X, user.Y, user.Z.ToString(CultureInfo.InvariantCulture), 0, kind,
            user.BotData.Gender.ToLowerInvariant(), 0, "", 0, false, 0, user.BotData.OwnerId,
            ResolveUsername(user.BotData.OwnerId), false, false);
    }

    private string ResolveUsername(int userId)
    {
        var online = clients.GetClientByUserId(userId)?.GetHabbo();

        if (online != null) {
            return online.Username;
        }

        var cached = cache.GenerateUser(userId);

        if (cached != null) {
            return cached.Username;
        }

        using var connection = database.Connection();
        var username = connection.QuerySingleOrDefault<string>("SELECT username FROM users WHERE id = @userId LIMIT 1", new { userId });

        return string.IsNullOrEmpty(username) ? "Unknown User" : username;
    }
}
