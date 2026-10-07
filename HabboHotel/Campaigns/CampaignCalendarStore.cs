using System.Collections.Immutable;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Campaigns
{
    public sealed class CampaignCalendarStore(IDatabase database) : ICampaignCalendarStore
    {
        private const string CampaignColumns = "id, name, image, starts_at AS StartsAt, days, lock_expired AS LockExpired, hc_duckets_multiplier AS ClubDucketsMultiplier";
        private const string RewardColumns = "id, campaign_id AS CampaignId, product_name AS ProductName, custom_image AS CustomImage, credits, duckets, diamonds, badge, item_id AS ItemId, hc_days AS ClubDays";

        public CalendarOffer? Find(string? name, DateTimeOffset now)
        {
            using var connection = database.Connection();
            var row = connection.QueryFirstOrDefault<CampaignRow>("SELECT " + CampaignColumns +
                " FROM campaign_calendars WHERE enabled = TRUE AND starts_at <= @now AND @now < DATE_ADD(starts_at, INTERVAL days DAY) AND (@name IS NULL OR BINARY name = BINARY @name) ORDER BY starts_at DESC, id DESC LIMIT 1",
                new { name, now = now.UtcDateTime });
            var campaign = row?.Capture();

            if (campaign == null || !campaign.Active(now)) {
                return null;
            }

            var rewards = connection.Query<RewardRow>("SELECT " + RewardColumns +
                " FROM campaign_calendar_rewards WHERE campaign_id = @id ORDER BY id", new { id = campaign.Id })
                .Select(reward => reward.Capture()).ToImmutableArray();

            return new(campaign, rewards);
        }

        public IReadOnlyList<int> Opened(int userId, int campaignId)
        {
            using var connection = database.Connection();

            return connection.Query<int>("SELECT day FROM user_calendar_claims WHERE user_id = @userId AND campaign_id = @campaignId ORDER BY day",
                new { userId, campaignId }).ToArray();
        }

        public CalendarGrant? Claim(int userId, CalendarCampaign campaign, CalendarReward reward, int day, bool staff,
            DateTimeOffset now, int credits, int duckets, int diamonds, string badge)
        {
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var storedCampaign = connection.QuerySingleOrDefault<CampaignRow>("SELECT " + CampaignColumns +
                " FROM campaign_calendars WHERE id = @id AND enabled = TRUE FOR UPDATE", new { id = campaign.Id }, transaction)?.Capture();
            var storedReward = connection.QuerySingleOrDefault<RewardRow>("SELECT " + RewardColumns +
                " FROM campaign_calendar_rewards WHERE id = @id AND campaign_id = @campaignId FOR UPDATE",
                new { id = reward.Id, campaignId = campaign.Id }, transaction)?.Capture();

            if (storedCampaign != campaign || storedReward != reward || !campaign.CanOpen(now, day, staff) ||
                connection.QuerySingleOrDefault<int?>("SELECT id FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction) == null ||
                connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_calendar_claims WHERE user_id = @userId AND campaign_id = @campaignId AND day = @day",
                    new { userId, campaignId = campaign.Id, day }, transaction) != 0) {
                return null;
            }

            // Every reward and the unique claim commit together. A failed statement leaves the day available.
            connection.Execute("INSERT INTO user_calendar_claims (user_id, campaign_id, day, reward_id, claimed_at) VALUES (@userId, @campaignId, @day, @rewardId, @now)",
                new { userId, campaignId = campaign.Id, day, rewardId = reward.Id, now = now.UtcDateTime }, transaction);
            connection.Execute("UPDATE users SET credits = @credits, activity_points = @duckets, vip_points = @diamonds WHERE id = @userId",
                new { userId, credits, duckets, diamonds }, transaction);

            if (badge.Length > 0) {
                connection.Execute("INSERT INTO user_badges (user_id, badge_id, badge_slot) VALUES (@userId, @badge, 0) ON DUPLICATE KEY UPDATE badge_slot = badge_slot",
                    new { userId, badge }, transaction);
            }

            uint itemId = 0;

            if (reward.ItemId != 0) {
                if (connection.ExecuteScalar<uint?>("SELECT id FROM furniture WHERE id = @id FOR UPDATE", new { id = reward.ItemId }, transaction) == null) {
                    return null;
                }

                connection.Execute("INSERT INTO items (base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data,limited_number,limited_stack) VALUES (@baseItem,@userId,0,0,0,0,'',0,'',0,0)",
                    new { baseItem = reward.ItemId, userId }, transaction);
                itemId = connection.QuerySingle<uint>("SELECT LAST_INSERT_ID()", transaction: transaction);
            }

            if (reward.ClubDays > 0 && ClubMembershipService.Extend(connection, transaction, userId, now, reward.ClubDays) == null) {
                return null;
            }

            transaction.Commit();

            return new(itemId, credits, duckets, diamonds, badge, reward.ClubDays);
        }

        private sealed class CampaignRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Image { get; set; } = "";
            public DateTimeOffset StartsAt { get; set; }
            public int Days { get; set; }
            public bool LockExpired { get; set; }
            public double ClubDucketsMultiplier { get; set; }
            public CalendarCampaign Capture() => new(Id, Name, Image, StartsAt, Days, LockExpired, ClubDucketsMultiplier);
        }

        private sealed class RewardRow
        {
            public int Id { get; set; }
            public int CampaignId { get; set; }
            public string ProductName { get; set; } = "";
            public string CustomImage { get; set; } = "";
            public int Credits { get; set; }
            public int Duckets { get; set; }
            public int Diamonds { get; set; }
            public string Badge { get; set; } = "";
            public uint ItemId { get; set; }
            public int ClubDays { get; set; }
            public CalendarReward Capture() => new(Id, CampaignId, ProductName, CustomImage, Credits, Duckets, Diamonds, Badge, ItemId, ClubDays);
        }
    }
}
