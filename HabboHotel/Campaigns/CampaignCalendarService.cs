using System.Globalization;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Campaign;
using Plus.Communication.Packets.Outgoing.Inventory.Badges;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Campaigns
{
    public sealed class CampaignCalendarService(ICampaignCalendarStore store, IItemDataManager items, IBadgeManager badges,
        IAccessControl permissions, IAccountSessionGate sessions, TimeProvider clock, ILogger<CampaignCalendarService> logger) : ICampaignCalendarService
    {
        public void Present(GameClient session)
        {
            var habbo = session.GetHabbo();

            if (habbo == null || habbo.AccessClosed || !Supported(session)) {
                return;
            }

            try {
                var now = clock.GetUtcNow();
                var offer = store.Find(null, now);

                if (offer == null) {
                    return;
                }

                var data = offer.Campaign.Capture(now, store.Opened(habbo.Id, offer.Campaign.Id));

                if (ReferenceEquals(session.GetHabbo(), habbo) && ReferenceEquals(habbo.Client, session) && !habbo.AccessClosed) {
                    session.Send(new CampaignCalendarDataComposer(data));
                }
            }
            catch (Exception exception) {
                logger.LogError(exception, "Loading the campaign calendar for user {UserId} failed", habbo.Id);
            }
        }

        public void Open(GameClient session, string campaignName, int day, bool staff = false)
        {
            var habbo = session.GetHabbo();

            // Calendar rewards land in the loaded inventory, so nothing is claimed without one.
            if (habbo?.Inventory is not { } inventory || campaignName.Length is 0 or > 64 || !Supported(session)) {
                return;
            }

            using var gate = sessions.Enter(habbo.Id);
            CalendarReward? reward = null;
            CalendarGrant? grant = null;
            ItemDefinition? definition = null;
            InventoryItem? item = null;
            var ducketsReward = 0;

            try {
                lock (habbo.WalletSync) {
                    if (habbo.AccessClosed || !ReferenceEquals(session.GetHabbo(), habbo) || !ReferenceEquals(habbo.Client, session) ||
                        staff && !habbo.Access.Can(PermissionKeys.CampaignCalendarForce)) {
                        return;
                    }

                    var now = clock.GetUtcNow();
                    var offer = store.Find(campaignName, now);

                    if (offer == null || !offer.Campaign.CanOpen(now, day, staff) || offer.Rewards.IsEmpty) {
                        session.Send(new CampaignCalendarDoorOpenedComposer(false));

                        return;
                    }

                    reward = offer.Rewards[Random.Shared.Next(offer.Rewards.Length)];

                    if (reward.Credits < 0 || reward.Duckets < 0 || reward.Diamonds < 0 || reward.ClubDays is < 0 or > 36500 ||
                        string.IsNullOrEmpty(reward.ProductName) || !double.IsFinite(offer.Campaign.ClubDucketsMultiplier) || offer.Campaign.ClubDucketsMultiplier < 0) {
                        session.Send(new CampaignCalendarDoorOpenedComposer(false));

                        return;
                    }

                    var badge = "";

                    if (reward.Badge.Length > 0) {
                        if (!badges.Badges.TryGetValue(reward.Badge.ToUpperInvariant(), out var badgeDefinition) ||
                            badgeDefinition.RequiredRight.Length > 0 && !habbo.Access.Can(badgeDefinition.RequiredRight)) {
                            session.Send(new CampaignCalendarDoorOpenedComposer(false));

                            return;
                        }

                        badge = badgeDefinition.Code;
                    }

                    // Calendar rewards are inventory furniture, not pets, bundles or boxes needing another product contract.
                    if (reward.ItemId != 0 && (!items.Items.TryGetValue(reward.ItemId, out definition) ||
                        definition.ProductType is not ("s" or "i") || definition.InteractionType is InteractionType.Teleport or InteractionType.Gift or InteractionType.Moodlight or InteractionType.Toner ||
                        definition.Category is FurniCategory.GuildFurni or FurniCategory.EcotronBox)) {
                        session.Send(new CampaignCalendarDoorOpenedComposer(false));

                        return;
                    }

                    if (definition != null) {
                        item = new InventoryItem
                        {
                            OwnerId = (uint)habbo.Id,
                            Definition = definition,
                            ExtraData = FurniExtraData.Load(definition, "", keepLegacy: false)
                        };
                    }

                    var pixels = reward.Duckets * (habbo.Access.Membership.Active(now) ? offer.Campaign.ClubDucketsMultiplier : 1);

                    if (pixels > int.MaxValue || (long)habbo.Credits + reward.Credits > int.MaxValue ||
                        (long)habbo.Duckets + (long)pixels > int.MaxValue || (long)habbo.Diamonds + reward.Diamonds > int.MaxValue) {
                        session.Send(new CampaignCalendarDoorOpenedComposer(false));

                        return;
                    }

                    ducketsReward = (int)pixels;
                    grant = store.Claim(habbo.Id, offer.Campaign, reward, day, staff, now,
                        habbo.Credits + reward.Credits, habbo.Duckets + ducketsReward, habbo.Diamonds + reward.Diamonds, badge);

                    if (grant == null) {
                        session.Send(new CampaignCalendarDoorOpenedComposer(false));

                        return;
                    }

                    // Publish the complete reward before any transport callback can synchronously save the wallet.
                    habbo.Credits = grant.Credits;
                    habbo.Duckets = grant.Duckets;
                    habbo.Diamonds = grant.Diamonds;

                    if (grant.Badge.Length > 0) {
                        inventory.Badges.AddBadge(new Badge(grant.Badge, 0));
                    }

                    if (grant.ItemId != 0 && item != null) {
                        item.Id = grant.ItemId;
                        inventory.Furniture.AddItem(item);
                    }
                }
            }
            catch (Exception exception) {
                logger.LogError(exception, "Opening campaign {Campaign} day {Day} for user {UserId} failed", campaignName, day, habbo.Id);

                if (!habbo.AccessClosed && ReferenceEquals(session.GetHabbo(), habbo)) {
                    session.Send(new CampaignCalendarDoorOpenedComposer(false));
                }

                return;
            }

            if (grant == null || reward == null) {
                return;
            }

            if (grant.ClubDays > 0) {
                permissions.Refresh(habbo.Id);
            }

            if (habbo.AccessClosed || !ReferenceEquals(session.GetHabbo(), habbo)) {
                return;
            }

            if (reward.Credits > 0) {
                session.Send(new CreditBalanceComposer(habbo.Credits));
            }

            if (ducketsReward > 0) {
                session.Send(new HabboActivityPointNotificationComposer(habbo.Duckets, ducketsReward));
            }

            if (reward.Diamonds > 0) {
                session.Send(new HabboActivityPointNotificationComposer(habbo.Diamonds, reward.Diamonds, 5));
            }

            if (grant.Badge.Length > 0) {
                session.Send(new BadgesComposer(BadgeInventorySnapshot.Capture(inventory.Badges.Badges.Values)));
                session.Send(new FurniListNotificationComposer(1, 4));
            }

            if (grant.ItemId != 0) {
                inventory.Furniture.SendInventory(session);
            }

            var product = reward.ProductName.Replace("%credits%", reward.Credits.ToString(CultureInfo.InvariantCulture))
                .Replace("%pixels%", ducketsReward.ToString(CultureInfo.InvariantCulture)).Replace("%points%", reward.Diamonds.ToString(CultureInfo.InvariantCulture))
                .Replace("%points_type%", "5").Replace("%badge%", grant.Badge).Replace("%subscription_type%", "HABBO_CLUB")
                .Replace("%subscription_days%", grant.ClubDays.ToString(CultureInfo.InvariantCulture)).Replace("%item%", definition?.ItemName ?? "");
            session.Send(new CampaignCalendarDoorOpenedComposer(true, product, reward.CustomImage, definition?.ItemName ?? ""));
        }

        private static bool Supported(GameClient session) => session.Revision?.InternalIdToOutgoingIdMapping?.ContainsKey(ServerPacketHeader.CampaignCalendarDataComposer) == true &&
            session.Revision.InternalIdToOutgoingIdMapping.ContainsKey(ServerPacketHeader.CampaignCalendarDoorOpenedComposer);
    }
}
