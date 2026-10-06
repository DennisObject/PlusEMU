using Plus.Database;
using Dapper;

namespace Plus.HabboHotel.Users.Inventory.Bots;

internal class BotLoader : IBotLoader
{
    private readonly IDatabase _database;

    public BotLoader(IDatabase database)
    {
        _database = database;
    }
    public List<Bot> GetBotsForUser(int userId)
    {
        using var connection = _database.Connection();

        return connection.Query<BotRow>("SELECT `id`, `user_id` AS UserId, `name`, `motto`, `look`, `gender` FROM `bots` " +
                "WHERE `user_id` = @userId AND `room_id` = 0 AND `ai_type` != 'pet'", new { userId })
            .Select(row => new Bot(checked((int)row.Id), checked((int)row.UserId), row.Name, row.Motto, row.Look, row.Gender)).ToList();
    }

    private sealed class BotRow
    {
        public uint Id { get; set; }
        public uint UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Motto { get; set; } = string.Empty;
        public string Look { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
    }
}
