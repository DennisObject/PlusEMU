using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Inventory.Badges;

internal class SetActivatedBadgesEvent : IPacketEvent
{
    private readonly BadgeManager _badgeManager;

    public SetActivatedBadgesEvent(BadgeManager badgeManager) => _badgeManager = badgeManager;

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var badgeUpdates = new List<(int slot, string badge)>();

        for (var i = 0; i < 5; i++)
        {
            var slot = packet.ReadInt();
            var badge = packet.ReadString();

            if (string.IsNullOrEmpty(badge) || slot < 1 || slot > 5)
            {
                continue;
            }

            badgeUpdates.Add((slot, badge));
        }

        var habbo = session.GetHabbo();
        var worn = new HashSet<string>(habbo.Inventory.Badges.EquippedBadges.Select(badge => badge.Code), StringComparer.OrdinalIgnoreCase);
        await _badgeManager.UpdateUserBadges(habbo, badgeUpdates);
        var added = habbo.Inventory.Badges.EquippedBadges.Count(badge => !worn.Contains(badge.Code));
        if (added > 0)
            RewardTrackManager.Current?.Progress(session, RewardTrackActions.WearBadge, added);

        var equippedBadges = habbo.Inventory.Badges.EquippedBadges;

        if (habbo.InRoom)
        {
            habbo.CurrentRoom?.SendPacket(new HabboUserBadgesComposer(habbo.Id, equippedBadges));
        }
        else
        {
            session.Send(new HabboUserBadgesComposer(habbo.Id, equippedBadges));
        }
    }
}
