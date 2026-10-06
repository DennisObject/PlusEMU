using System.Data;
using Dapper;
using Plus.HabboHotel.Users.Inventory.Bots;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Catalog;

[Singleton]
public interface ICatalogBotPurchaseStore
{
    Bot Create(IDbConnection connection, IDbTransaction transaction, CatalogBot preset, int ownerId);
}

public sealed class CatalogBotPurchaseStore : ICatalogBotPurchaseStore
{
    public Bot Create(IDbConnection connection, IDbTransaction transaction, CatalogBot preset, int ownerId)
    {
        connection.Execute(
            "INSERT INTO bots (user_id,name,motto,look,gender,ai_type) VALUES (@ownerId,@name,@motto,@look,@gender,@aiType)",
            new
            {
                ownerId,
                preset.Name,
                preset.Motto,
                look = preset.Figure,
                preset.Gender,
                aiType = preset.AiType
            }, transaction);
        var id = connection.QuerySingle<int>("SELECT LAST_INSERT_ID()", transaction: transaction);

        return new(id, ownerId, preset.Name, preset.Motto, preset.Figure, preset.Gender);
    }
}
