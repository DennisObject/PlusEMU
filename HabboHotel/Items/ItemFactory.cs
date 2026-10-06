using System.Data;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items;

public class ItemFactory : IItemFactory
{
    private readonly IDatabase _database;
    public static ItemFactory Instance { get; set; }

    public ItemFactory(IDatabase database)
    {
        _database = database;
    }

    public Item CreateSingleItemNullable(ItemDefinition definition, Habbo habbo, string extraData, string displayFlags, int groupId = 0, uint limitedNumber = 0, uint limitedStack = 0)
    {
        if (definition == null) throw new InvalidOperationException("Data cannot be null.");
        var item = new Item()
        {
            OwnerId = (uint)habbo.Id,
            Definition = definition,
            ExtraData = FurniExtraData.Load(definition, extraData, keepLegacy: true),
            UniqueNumber = limitedNumber,
            UniqueSeries = limitedStack,
            GroupId = groupId
        };
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            "INSERT INTO `items` (base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data,`limited_number`,`limited_stack`) VALUES (@did,@uid,0,0,0,0,'',0,@extraData,@limitedNumber,@limitedStack)",
            new { did = definition.Id, uid = habbo.Id, extraData, limitedNumber, limitedStack }, transaction);
        item.Id = connection.QuerySingle<uint>("SELECT LAST_INSERT_ID()", transaction: transaction);
        if (groupId > 0)
        {
            connection.Execute("INSERT INTO `items_groups` (`id`, `group_id`) VALUES (@id, @groupId)", new { id = item.Id, groupId }, transaction);
        }
        transaction.Commit();
        return item;
    }

    public Item CreateSingleItem(ItemDefinition definition, Habbo habbo, string extraData, string displayFlags, uint itemId, uint limitedNumber = 0, uint limitedStack = 0)
    {
        if (definition == null) throw new InvalidOperationException("Data cannot be null.");

        var item = new Item()
        {
            Id = itemId,
            OwnerId = (uint)habbo.Id,
            Definition = definition,
            ExtraData = FurniExtraData.Load(definition, extraData, keepLegacy: true),
            UniqueNumber = limitedNumber,
            UniqueSeries = limitedStack
        };
        using var connection = _database.Connection();
        connection.Execute(
            "INSERT INTO `items` (`id`,base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data,`limited_number`,`limited_stack`) VALUES (@itemId,@did,@uid,0,0,0,0,'',0,@extraData,@limitedNumber,@limitedStack)",
            new { itemId, did = definition.Id, uid = habbo.Id, extraData, limitedNumber, limitedStack });
        return item;
    }

    public Item CreateGiftItem(ItemDefinition definition, Habbo habbo, string extraData, string displayFlags, int itemId, uint limitedNumber = 0, uint limitedStack = 0)
    {
        if (definition == null) throw new InvalidOperationException("Data cannot be null.");
        var item = new Item()
        {
            OwnerId = (uint)habbo.Id,
            Definition = definition,
            ExtraData = FurniExtraData.Load(definition, extraData, keepLegacy: true),
            UniqueNumber = limitedNumber,
            UniqueSeries = limitedStack,
        };
        using var connection = _database.Connection();
        connection.Execute(
            "INSERT INTO `items` (`id`,base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data,`limited_number`,`limited_stack`) VALUES (@itemId,@did,@uid,0,0,0,0,'',0,@extraData,@limitedNumber,@limitedStack)",
            new { itemId, did = definition.Id, uid = habbo.Id, extraData, limitedNumber, limitedStack });
        return item;
    }

    public List<Item> CreateMultipleItems(ItemDefinition definition, Habbo habbo, string extraData, int amount, int groupId = 0) =>
        CreateMultipleItems(definition, habbo.Id, extraData, amount, groupId);

    public List<Item> CreateMultipleItems(ItemDefinition definition, int ownerId, string extraData, int amount, int groupId = 0)
    {
        if (definition == null) throw new InvalidOperationException("Data cannot be null.");
        var items = new List<Item>();
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        for (var i = 0; i < amount; i++)
        {
            connection.Execute("INSERT INTO `items` (base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data) VALUES(@did,@ownerId,0,0,0,0,'',0,@extraData)",
                new { did = definition.Id, ownerId, extraData }, transaction);

            var item = new Item()
            {
                Id = connection.QuerySingle<uint>("SELECT LAST_INSERT_ID()", transaction: transaction),
                OwnerId = (uint)ownerId,
                Definition = definition,
                ExtraData = FurniExtraData.Load(definition, extraData, keepLegacy: true),
                GroupId = groupId
            };
            if (groupId > 0)
            {
                connection.Execute("INSERT INTO `items_groups` (`id`, `group_id`) VALUES (@id, @groupId)", new { id = item.Id, groupId }, transaction);
            }
            items.Add(item);
        }
        transaction.Commit();
        return items;
    }

    public List<Item> CreateTeleporterItems(ItemDefinition definition, Habbo habbo, int groupId = 0)
    {
        var items = new List<Item>();
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        const string insert = "INSERT INTO `items` (base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data) VALUES(@did,@uid,0,0,0,0,'',0,@extraData)";
        connection.Execute(insert, new { did = definition.Id, uid = habbo.Id, extraData = "" }, transaction);
        var item1Id = connection.QuerySingle<uint>("SELECT LAST_INSERT_ID()", transaction: transaction);
        connection.Execute(insert, new { did = definition.Id, uid = habbo.Id, extraData = item1Id.ToString() }, transaction);
        var item2Id = connection.QuerySingle<uint>("SELECT LAST_INSERT_ID()", transaction: transaction);

        var item1 = new Item()
        {
            Id = item1Id,
            OwnerId = (uint)habbo.Id,
            Definition = definition,
            ExtraData = new LegacyDataFormat(),
            GroupId = groupId
        };
        var item2 = new Item()
        {
            Id = item2Id,
            OwnerId = (uint)habbo.Id,
            Definition = definition,
            ExtraData = new LegacyDataFormat(),
            GroupId = groupId
        };
        connection.Execute("INSERT INTO `room_items_tele_links` (`tele_one_id`, `tele_two_id`) VALUES (@item1Id,@item2Id),(@item2Id,@item1Id)",
            new { item1Id, item2Id }, transaction);
        transaction.Commit();
        items.Add(item1);
        items.Add(item2);
        return items;
    }

    public void CreateMoodlightData(Item item)
    {
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO `room_items_moodlight` (`item_id`, `enabled`, `current_preset`, `preset_one`, `preset_two`, `preset_three`) VALUES (@itemId,FALSE,1,@preset,@preset,@preset)",
            new { itemId = item.Id, preset = "#000000,255,0" });
    }

    public void CreateTonerData(Item item)
    {
        using var connection = _database.Connection();
        connection.Execute("INSERT INTO `room_items_toner` (`id`, `data1`, `data2`, `data3`, `enabled`) VALUES (@id,0,0,0,FALSE)", new { id = item.Id });
    }
}
