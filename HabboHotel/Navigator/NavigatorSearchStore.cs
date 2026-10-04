using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Navigator;

public interface INavigatorSearchStore
{
    IReadOnlyList<uint> FindByOwnerName(string username);
    IReadOnlyList<uint> FindByCaption(string captionPrefix);
    IReadOnlyList<uint> FindWithRights(int userId, int limit);
}

public sealed class NavigatorSearchStore(IDatabase database) : INavigatorSearchStore
{
    public IReadOnlyList<uint> FindByOwnerName(string username)
    {
        using var connection = database.Connection();
        return connection.Query<uint>("SELECT rooms.id FROM rooms INNER JOIN users ON users.id=rooms.owner WHERE users.username=@username AND rooms.state<>'invisible' ORDER BY rooms.users_now DESC LIMIT 50", new { username }).ToArray();
    }

    public IReadOnlyList<uint> FindByCaption(string captionPrefix)
    {
        using var connection = database.Connection();
        return connection.Query<uint>("SELECT id FROM rooms WHERE caption LIKE @query AND state<>'invisible' ORDER BY users_now DESC LIMIT 50", new { query = $"{captionPrefix}%" }).ToArray();
    }

    public IReadOnlyList<uint> FindWithRights(int userId, int limit)
    {
        using var connection = database.Connection();
        return connection.Query<uint>("SELECT room_id FROM room_rights WHERE user_id=@userId LIMIT @limit", new { userId, limit }).ToArray();
    }
}
