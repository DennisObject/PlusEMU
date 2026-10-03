using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

public class ItemDataManager : IItemDataManager
{
    private readonly ILogger<ItemDataManager> _logger;
    private readonly IDatabase _database;
    public Dictionary<int, uint> Gifts { get; private set; } = new(0); //<SpriteId, Item>
    public Dictionary<uint, ItemDefinition> Items { get; private set; } = new(0);

    public ItemDataManager(ILogger<ItemDataManager> logger, IDatabase database)
    {
        _logger = logger;
        _database = database;
    }

    // Builds new tables and swaps them in, so a reload never shows readers a half-loaded furniture table.
    public void Init()
    {
        var gifts = new Dictionary<int, uint>();
        var items = new Dictionary<uint, ItemDefinition>();
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT * FROM `furniture`");
            var itemData = dbClient.GetTable();
            if (itemData != null)
            {
                foreach (DataRow row in itemData.Rows)
                {
                    try
                    {
                        var productType = Convert.ToString(row["type"])?.ToLowerInvariant() ?? "s";
                        var definition = new ItemDefinition
                        {
                            Id = Convert.ToUInt32(row["id"]),
                            SpriteId = Convert.ToInt32(row["sprite_id"]),
                            ItemName = Convert.ToString(row["item_name"]),
                            PublicName = Convert.ToString(row["public_name"]),
                            ProductType = productType,
                            Type = productType == "s" ? ItemType.Floor : ItemType.Wall,
                            Width = Convert.ToInt32(row["width"]),
                            Length = Convert.ToInt32(row["length"]),
                            Height = FurnitureNumbers.FromCell(row["stack_height"]),
                            Stackable = row["can_stack"].ToString() == "1",
                            Walkable = row["is_walkable"].ToString() == "1",
                            IsSeat = row["can_sit"].ToString() == "1",
                            AllowEcotronRecycle = row["allow_recycle"].ToString() == "1",
                            AllowTrade = row["allow_trade"].ToString() == "1",
                            AllowMarketplaceSell = row["allow_marketplace_sell"].ToString() == "1",
                            AllowGift = row["allow_gift"].ToString() == "1",
                            AllowInventoryStack = row["allow_inventory_stack"].ToString() == "1",
                            InteractionType = ReadInteractionType(Convert.ToString(row["item_name"]), Convert.ToString(row["interaction_type"]), ReadWiredType(row["wired_id"])),
                            WiredType = ReadWiredType(row["wired_id"]),
                            InteractionName = Convert.ToString(row["interaction_type"]) ?? string.Empty,
                            BehaviourData = Convert.ToInt32(row["behaviour_data"]),
                            Modes = Convert.ToInt32(row["interaction_modes_count"]),
                            VendingIds = (!string.IsNullOrEmpty(Convert.ToString(row["vending_ids"])) && Convert.ToString(row["vending_ids"]) != "0")
                                ? Convert.ToString(row["vending_ids"]).Split(",").Select(FurnitureNumbers.ParseInt).ToList()
                                : new(0),
                            AdjustableHeights = (!string.IsNullOrEmpty(Convert.ToString(row["height_adjustable"])) && Convert.ToString(row["height_adjustable"]) != "0")
                                ? Convert.ToString(row["height_adjustable"]).Split(",").Select(FurnitureNumbers.Parse).ToList()
                                : new(0),
                            EffectId = Convert.ToInt32(row["effect_id"]),
                            IsRare = row["is_rare"].ToString() == "1",
                            ExtraRot = row["extra_rot"].ToString() == "1",
                        };

                        gifts.TryAdd(definition.SpriteId, definition.Id);
                        items.Add(definition.Id, definition);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                        Console.ReadKey();
                        //Logging.WriteLine("Could not load item #" + Convert.ToInt32(Row[0]) + ", please verify the data is okay.");
                    }
                }
            }
        }
        Gifts = gifts;
        Items = items;
        _logger.LogInformation("Item Manager -> LOADED");
    }

    public ItemDefinition GetItemByName(string name)
    {
        foreach (var entry in Items)
        {
            var item = entry.Value;
            if (item.ItemName == name)
                return item;
        }
        return null;
    }

    internal static InteractionType ReadInteractionType(string itemName, string interactionType, WiredBoxType wiredType = WiredBoxType.None)
    {
        if (itemName is "sb_rail" or "sb_ramp" or "sb_block")
            return InteractionType.Skateboard;
        // Explicit generic legacy rows keep their category; a canonical descriptor still guides modern factories.
        if (WiredBoxTypeUtility.IsLegacyConstructible(wiredType)
            && interactionType.ToLowerInvariant() is "wired_effect" or "wired_trigger" or "wired_condition")
            return InteractionTypes.GetTypeFromString(interactionType);
        if (WiredBoxRegistry.TryGet(interactionType, out _) || WiredBoxRegistry.TryGet(itemName, out _))
            return InteractionTypes.GetTypeFromString(WiredBoxRegistry.TryGet(interactionType, out _) ? interactionType : itemName);
        return InteractionTypes.GetTypeFromString(interactionType);
    }

    internal static WiredBoxType ReadWiredType(object cell)
    {
        if (cell is null or DBNull)
            return WiredBoxType.None;
        return int.TryParse(Convert.ToString(cell, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? WiredBoxTypeUtility.FromWiredId(id)
            : WiredBoxType.None;
    }
}