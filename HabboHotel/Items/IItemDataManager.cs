namespace Plus.HabboHotel.Items;

public interface IItemDataManager
{
    void Init();
    ItemDefinition? GetItemByName(string name);
    Dictionary<int, uint> Gifts { get; } //<SpriteId, Item>
    Dictionary<uint, ItemDefinition> Items { get; }
}