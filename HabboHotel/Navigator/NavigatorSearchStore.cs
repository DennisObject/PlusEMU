using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Navigator;

public interface INavigatorSearchStore
{
    IReadOnlyList<uint> FindByOwnerName(string username);
    IReadOnlyList<NavigatorRoomReference> FindByCaption(string captionPrefix);
    IReadOnlyList<uint> FindWithRights(int userId, int limit);
}

public sealed class NavigatorSearchStore(IDatabase database) : INavigatorSearchStore
{
    public IReadOnlyList<uint> FindByOwnerName(string username)
    {
        using var connection = database.Connection();

        return connection.Query<uint>("SELECT id FROM rooms WHERE owner=(SELECT id FROM users WHERE username=@username LIMIT 1) AND state<>'invisible' ORDER BY users_now DESC LIMIT 50", new { username }).ToArray();
    }

    public IReadOnlyList<NavigatorRoomReference> FindByCaption(string captionPrefix)
    {
        using var connection = database.Connection();

        return connection.Query<NavigatorRoomReference>("SELECT id,state<>'invisible' AS Visible FROM rooms WHERE caption LIKE @query ORDER BY users_now DESC LIMIT 50", new { query = $"{captionPrefix}%" }).ToArray();
    }

    public IReadOnlyList<uint> FindWithRights(int userId, int limit)
    {
        using var connection = database.Connection();

        return connection.Query<uint>("SELECT room_id FROM room_rights WHERE user_id=@userId LIMIT @limit", new { userId, limit }).ToArray();
    }
}

public sealed class NavigatorRoomReference
{
    public int Id { get; init; }
    public bool Visible { get; init; }
}
