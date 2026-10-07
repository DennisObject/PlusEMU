using System.Collections.Immutable;
using Plus.HabboHotel.Badges.Rarity;
using Plus.HabboHotel.Users.Badges;

namespace Plus.HabboHotel.Users.Inventory.Badges;

public sealed record BadgeSlotSnapshot(string Code, int Slot, BadgeRarity Rarity = default);
public sealed record BadgeInventorySnapshot(ImmutableArray<BadgeSlotSnapshot> Badges, ImmutableArray<BadgeSlotSnapshot> Equipped)
{
    public static BadgeInventorySnapshot Capture(IEnumerable<Badge> badges, BadgeRarityTable? rarity = null)
    {
        rarity ??= BadgeRarityTable.Current;
        var captured = badges.Select(badge => new BadgeSlotSnapshot(badge.Code, badge.Slot, rarity.Get(badge.Code))).ToImmutableArray();

        return new(captured, captured.Where(badge => badge.Slot > 0).OrderBy(badge => badge.Slot).ToImmutableArray());
    }
}
