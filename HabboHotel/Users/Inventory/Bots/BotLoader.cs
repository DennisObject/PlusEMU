using System.Data;
using Plus.Database;

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
        var b = new List<Bot>();
        DataTable? dBots = null;
        using var dbClient = _database.GetQueryReactor();
        dbClient.SetQuery($"SELECT `id`,`user_id`,`name`,`motto`,`look`,`gender`FROM `bots` WHERE `user_id` = '{userId}' AND `room_id` = '0' AND `ai_type` != 'pet'");
        dBots = dbClient.GetTable();
        if (dBots != null)
        {
            foreach (DataRow dRow in dBots.Rows)
            {
                b.Add(new(Convert.ToInt32(dRow["id"]), Convert.ToInt32(dRow["user_id"]), Convert.ToString(dRow["name"]),
                    Convert.ToString(dRow["motto"]), Convert.ToString(dRow["look"]), Convert.ToString(dRow["gender"])));
            }
        }
        return b;
    }
}