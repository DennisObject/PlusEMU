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
        // The preset columns are NOT NULL; reject an incomplete preset before writing the bot.
        var name = Required(preset, preset.Name, "name");
        var motto = Required(preset, preset.Motto, "motto");
        var figure = Required(preset, preset.Figure, "figure");
        var gender = Required(preset, preset.Gender, "gender");
        connection.Execute(
            "INSERT INTO bots (user_id,name,motto,look,gender,ai_type) VALUES (@ownerId,@name,@motto,@look,@gender,@aiType)",
            new { ownerId, name, motto, look = figure, gender, aiType = preset.AiType }, transaction);
        var id = connection.QuerySingle<int>("SELECT LAST_INSERT_ID()", transaction: transaction);

        return new(id, ownerId, name, motto, figure, gender);
    }

    private static string Required(CatalogBot preset, string? value, string column) =>
        value ?? throw new InvalidOperationException($"Catalog bot preset {preset.Id} has no {column}.");
}
