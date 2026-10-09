using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Bots;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Users.Inventory.Pets;

namespace Plus.HabboHotel.Users.Inventory;

public class InventoryComponent
{
    // A part the loader does not set is an empty inventory, never a missing one.
    public BadgesInventoryComponent Badges { get; init; } = new(new());
    public FurnitureInventoryComponent Furniture { get; init; } = new([], []);
    public PetsInventoryComponent Pets { get; init; } = new([]);
    public BotInventoryComponent Bots { get; init; } = new([]);
}
