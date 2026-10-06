using System.Data;
using Dapper;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Catalog;

[Singleton]
public interface ICatalogGiftStore
{
    InventoryItem Create(
        IDbConnection connection,
        IDbTransaction transaction,
        int recipientId,
        ItemDefinition presentDefinition,
        ItemDefinition contentDefinition,
        string presentExtraData,
        string contentExtraData);
}

public sealed class CatalogGiftStore : ICatalogGiftStore
{
    public InventoryItem Create(
        IDbConnection connection,
        IDbTransaction transaction,
        int recipientId,
        ItemDefinition presentDefinition,
        ItemDefinition contentDefinition,
        string presentExtraData,
        string contentExtraData)
    {
        var itemId = connection.ExecuteScalar<uint>(
            "INSERT INTO items (base_item, user_id, extra_data) VALUES (@baseId, @recipientId, @extraData); SELECT LAST_INSERT_ID()",
            new
            {
                baseId = presentDefinition.Id,
                recipientId,
                extraData = presentExtraData
            },
            transaction);
        connection.Execute(
            "INSERT INTO user_presents (item_id, base_id, extra_data) VALUES (@itemId, @baseId, @extraData)",
            new
            {
                itemId,
                baseId = contentDefinition.Id,
                extraData = contentExtraData
            },
            transaction);

        return new InventoryItem
        {
            Id = itemId,
            OwnerId = (uint)recipientId,
            Definition = presentDefinition,
            ExtraData = FurniExtraData.Load(presentDefinition, presentExtraData, keepLegacy: true)
        };
    }
}
