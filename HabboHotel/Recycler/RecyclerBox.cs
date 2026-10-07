using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Recycler;

internal static class RecyclerBox
{
    public const string ClassName = "ecotron_box";
    public const int SpriteId = 3095;

    public static bool IsIdentity(ItemDefinition definition) => definition.ItemName == ClassName && definition.SpriteId == SpriteId;
    public static bool IsDefinition(ItemDefinition definition) => IsIdentity(definition)
        && definition.Type == ItemType.Floor && definition.ProductType == "s" && definition.InteractionType == InteractionType.None;
    public static ItemDefinition? Find(IEnumerable<ItemDefinition> definitions)
    {
        var boxes = definitions.Where(IsIdentity).Take(2).ToArray();
        return boxes.Length == 1 && IsDefinition(boxes[0]) ? boxes[0] : null;
    }
}
