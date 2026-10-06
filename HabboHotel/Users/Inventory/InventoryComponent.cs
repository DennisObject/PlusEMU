using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Bots;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Users.Inventory.Pets;

namespace Plus.HabboHotel.Users.Inventory;

public class InventoryComponent
{
    public BadgesInventoryComponent Badges
    {
        get; init;
    }
    public FurnitureInventoryComponent Furniture
    {
        get; init;
    }
    public PetsInventoryComponent Pets
    {
        get; init;
    }
    public BotInventoryComponent Bots
    {
        get; init;
    }
}
