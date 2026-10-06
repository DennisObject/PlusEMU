using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items;

public interface IItemFactory
{
    Item CreateSingleItemNullable(ItemDefinition definition, Habbo habbo, string extraData, string displayFlags, int groupId = 0, uint limitedNumber = 0, uint limitedStack = 0);
    Item CreateSingleItem(ItemDefinition definition, Habbo habbo, string extraData, string displayFlags, uint itemId, uint limitedNumber = 0, uint limitedStack = 0);
    Item CreateGiftItem(ItemDefinition definition, Habbo habbo, string extraData, string displayFlags, int itemId, uint limitedNumber = 0, uint limitedStack = 0);
    List<Item> CreateMultipleItems(ItemDefinition definition, Habbo habbo, string extraData, int amount, int groupId = 0);
    List<Item> CreateMultipleItems(ItemDefinition definition, int ownerId, string extraData, int amount, int groupId = 0);
    List<Item> CreateTeleporterItems(ItemDefinition definition, Habbo habbo, int groupId = 0);
    void CreateMoodlightData(Item item);
    void CreateTonerData(Item item);
}
