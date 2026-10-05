using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users.Inventory.Badges;

namespace Plus.HabboHotel.Badges;

public sealed record BadgeEquipmentChange(ImmutableArray<BadgeSlotSnapshot> Equipped, int Added);

public interface IBadgeEquipmentService
{
    Task Set(GameClient session, ImmutableArray<BadgeSlotSnapshot> requested);
    Task Show(GameClient session, int userId);
}

public sealed class BadgeEquipmentService(BadgeManager badges, IRewardTrackManager rewards) : IBadgeEquipmentService
{
    public async Task Set(GameClient session, ImmutableArray<BadgeSlotSnapshot> requested)
    {
        var habbo = session.GetHabbo();
        var updates = requested.Where(badge => !string.IsNullOrEmpty(badge.Code) && badge.Slot is >= 1 and <= 5)
            .Select(badge => (slot: badge.Slot, badge: badge.Code)).ToList();
        var change = await badges.UpdateUserBadges(habbo, updates);
        if (change == null)
            return;
        if (change.Added > 0)
            rewards.Progress(session, RewardTrackActions.WearBadge, change.Added);
        var packet = new HabboUserBadgesComposer(habbo.Id, change.Equipped);
        if (habbo.InRoom)
            habbo.CurrentRoom?.SendPacket(packet);
        else
            session.Send(packet);
    }

    public async Task Show(GameClient session, int userId)
    {
        var equipped = await badges.GetEquippedBadgesForUserAsync(userId);
        session.Send(new HabboUserBadgesComposer(userId, BadgeInventorySnapshot.Capture(equipped).Equipped));
    }
}
