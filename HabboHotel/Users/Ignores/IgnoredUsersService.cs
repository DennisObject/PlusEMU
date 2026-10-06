using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Ignores;

internal class IgnoredUsersService : IIgnoredUsersService
{
    private readonly IDatabase _database;

    public IgnoredUsersService(IDatabase database)
    {
        _database = database;
    }

    public async Task<List<string>> GetIgnoredUsersByName(IReadOnlyCollection<int> userIds)
    {
        if (!userIds.Any()) {
            return new();
        }

        using var connection = _database.Connection();

        return (await connection.QueryAsync<string>("SELECT username FROM users WHERE id in @userIds", new { userIds })).ToList();
    }
}
