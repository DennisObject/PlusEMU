using System.Collections.Immutable;
using Plus.HabboHotel.Users.Badges;

namespace Plus.HabboHotel.Users.Inventory.Badges;

public sealed record BadgeSlotSnapshot(string Code, int Slot);
public sealed record BadgeInventorySnapshot(ImmutableArray<string> Codes, ImmutableArray<BadgeSlotSnapshot> Equipped)
{
    public static BadgeInventorySnapshot Capture(IEnumerable<Badge> badges)
    {
        var captured = badges.Select(badge => new BadgeSlotSnapshot(badge.Code, badge.Slot)).ToArray();

        return new(captured.Select(badge => badge.Code).ToImmutableArray(),
            captured.Where(badge => badge.Slot > 0).OrderBy(badge => badge.Slot).ToImmutableArray());
    }
}
