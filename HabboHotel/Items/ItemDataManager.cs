using Plus.Core;
using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Dapper;

namespace Plus.HabboHotel.Items;

public class ItemDataManager : IItemDataManager, IStartable
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

    public int StartOrder => 10;
    public Task Start() => LoadAsync();

    public void Init() => LoadAsync().GetAwaiter().GetResult();

    private async Task LoadAsync()
    {
        using var connection = _database.Connection();
        var rows = await connection.QueryAsync("SELECT * FROM `furniture`");
        var table = new DataTable();

        foreach (var values in rows.Cast<IDictionary<string, object?>>())
        {
            if (table.Columns.Count == 0)
            {
                foreach (var name in values.Keys)
                {
                    table.Columns.Add(name, typeof(object));
                }
            }

            var row = table.NewRow();

            foreach (var value in values)
            {
                row[value.Key] = value.Value ?? DBNull.Value;
            }

            table.Rows.Add(row);
        }

        Load(table);
    }

    // Builds new tables and swaps them in, so a reload never shows readers a half-loaded furniture table.
    internal void Load(DataTable? itemData)
    {
        var gifts = new Dictionary<int, uint>();
        var items = new Dictionary<uint, ItemDefinition>();

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
                        Stackable = FurnitureNumbers.BooleanFromCell(row["can_stack"]),
                        Walkable = FurnitureNumbers.BooleanFromCell(row["is_walkable"]),
                        IsSeat = FurnitureNumbers.BooleanFromCell(row["can_sit"]),
                        AllowEcotronRecycle = FurnitureNumbers.BooleanFromCell(row["allow_recycle"]),
                        AllowTrade = FurnitureNumbers.BooleanFromCell(row["allow_trade"]),
                        AllowMarketplaceSell = FurnitureNumbers.BooleanFromCell(row["allow_marketplace_sell"]),
                        AllowGift = FurnitureNumbers.BooleanFromCell(row["allow_gift"]),
                        AllowInventoryStack = FurnitureNumbers.BooleanFromCell(row["allow_inventory_stack"]),
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
                        IsRare = FurnitureNumbers.BooleanFromCell(row["is_rare"]),
                        ExtraRot = FurnitureNumbers.BooleanFromCell(row["extra_rot"]),
                    };

                    gifts.TryAdd(definition.SpriteId, definition.Id);
                    items.Add(definition.Id, definition);
                }
                // A bad row is skipped: reloads also run in the background, where waiting for a key would hang them.
                catch (Exception e)
                {
                    _logger.LogError(e, "Skipped furniture #{Id}: the row has invalid data", row["id"]);
                }
            }
        }

        Gifts = gifts;
        Items = items;
        _logger.LogInformation("Item Manager -> LOADED");
    }

    public ItemDefinition? GetItemByName(string name)
    {
        foreach (var entry in Items)
        {
            var item = entry.Value;

            if (item.ItemName == name)
            {
                return item;
            }
        }

        return null;
    }

    internal static InteractionType ReadInteractionType(string itemName, string interactionType, WiredBoxType wiredType = WiredBoxType.None)
    {
        if (itemName is "sb_rail" or "sb_ramp" or "sb_block")
        {
            return InteractionType.Skateboard;
        }

        // Explicit generic legacy rows keep their category; a canonical descriptor still guides modern factories.
        if (WiredBoxTypeUtility.IsLegacyConstructible(wiredType)
            && interactionType.ToLowerInvariant() is "wired_effect" or "wired_trigger" or "wired_condition")
        {
            return InteractionTypes.GetTypeFromString(interactionType);
        }

        if (WiredBoxRegistry.TryGet(interactionType, out _) || WiredBoxRegistry.TryGet(itemName, out _))
        {
            return InteractionTypes.GetTypeFromString(WiredBoxRegistry.TryGet(interactionType, out _) ? interactionType : itemName);
        }

        return InteractionTypes.GetTypeFromString(interactionType);
    }

    internal static WiredBoxType ReadWiredType(object cell)
    {
        if (cell is null or DBNull)
        {
            return WiredBoxType.None;
        }

        return int.TryParse(Convert.ToString(cell, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? WiredBoxTypeUtility.FromWiredId(id)
            : WiredBoxType.None;
    }
}
