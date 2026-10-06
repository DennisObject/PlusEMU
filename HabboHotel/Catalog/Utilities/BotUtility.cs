using Dapper;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users.Inventory.Bots;

namespace Plus.HabboHotel.Catalog.Utilities;

public static class BotUtility
{
    public static Bot? CreateBot(ItemDefinition itemDefinition, int ownerId)
    {
        if (!PlusEnvironment.Game.Catalog.TryGetBot(itemDefinition.Id, out var cataBot))
            return null;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            "INSERT INTO bots (user_id,name,motto,look,gender,ai_type) VALUES (@ownerId,@name,@motto,@look,@gender,@aiType)",
            new { ownerId, cataBot.Name, cataBot.Motto, look = cataBot.Figure, cataBot.Gender, aiType = cataBot.AiType }, transaction);
        var id = connection.QuerySingle<int>("SELECT LAST_INSERT_ID()", transaction: transaction);
        transaction.Commit();
        return new(id, ownerId, cataBot.Name, cataBot.Motto, cataBot.Figure, cataBot.Gender);
    }


    public static BotAiType GetAiFromString(string type)
    {
        switch (type)
        {
            case "pet":
                return BotAiType.Pet;
            case "generic":
                return BotAiType.Generic;
            case "bartender":
                return BotAiType.Bartender;
            default:
                return BotAiType.Generic;
        }
    }
}
