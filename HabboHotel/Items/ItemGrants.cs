namespace Plus.HabboHotel.Items;

/// <summary>Which definitions staff and CMS grants may create as bare inventory items.</summary>
public static class ItemGrants
{
    // These need linked rows or data a bare inventory item cannot carry.
    private static readonly HashSet<InteractionType> NeedLinkedData = new()
    {
        InteractionType.Teleport, InteractionType.Moodlight, InteractionType.Toner, InteractionType.Gift, InteractionType.Trophy,
        InteractionType.GuildItem, InteractionType.GuildGate, InteractionType.GuildForum, InteractionType.BadgeDisplay,
        InteractionType.Badge, InteractionType.Pet, InteractionType.Bot, InteractionType.PurchasableClothing
    };

    public static bool NeedsLinkedData(ItemDefinition definition) => NeedLinkedData.Contains(definition.InteractionType);
}
