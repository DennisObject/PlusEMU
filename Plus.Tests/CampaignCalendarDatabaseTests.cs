using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Campaigns;
using Plus.HabboHotel.Subscriptions;
using Xunit;

namespace Plus.Tests
{
    public sealed class CampaignCalendarDatabaseFactAttribute : FactAttribute
    {
        public CampaignCalendarDatabaseFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CAMPAIGN_CALENDAR_DATABASE"))) {
                Skip = "Set CAMPAIGN_CALENDAR_DATABASE to an isolated MariaDB server with CREATE DATABASE permission.";
            }
        }
    }

    public sealed class CampaignCalendarDatabaseTests
    {
        [CampaignCalendarDatabaseFact]
        public void NativeSchemaAtomicBundleReopensIdempotentlyAndFailedWritesPreserveTheDoor()
        {
            using var fixture = new Database();
            var connection = fixture.Connection;
            var service = new CampaignCalendarStore(fixture);
            var now = CampaignCalendarTests.Now;
            var offer = Assert.IsType<CalendarOffer>(service.Find("configured", now));
            Assert.Null(service.Find("CONFIGURED", now));
            Assert.Null(service.Find("configured", offer.Campaign.StartsAt.AddDays(24)));
            Assert.Empty(service.Opened(7, 1));
            var reward = Assert.Single(offer.Rewards);
            var result = service.Claim(7, offer.Campaign, reward, 6, false, now, 13, 24, 35, "TEST");
            Assert.NotNull(result);
            Assert.Equal((13, 24, 35), connection.QuerySingle<(int, int, int)>("SELECT credits,activity_points,vip_points FROM users WHERE id=7"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_badges WHERE user_id=7 AND badge_id='TEST'"));
            Assert.Equal(10u, connection.QuerySingle<uint>("SELECT base_item FROM items WHERE id=@id AND user_id=7 AND room_id=0", new { id = result.ItemId }));
            var membership = connection.QuerySingle<ClubMembershipRow>("SELECT " + ClubMembership.Columns + " FROM user_club_memberships WHERE user_id=7").ToMembership();
            Assert.Equal(now.AddDays(2), membership.ExpiresAt);
            Assert.Equal(now, membership.StartedAt);
            Assert.Equal(new[] { 6 }, new CampaignCalendarStore(fixture).Opened(7, 1));
            Assert.Null(service.Claim(7, offer.Campaign, reward, 6, false, now, 16, 28, 40, "TEST"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM club_membership_intervals"));
            Assert.Equal(now, connection.QuerySingle<DateTimeOffset>("SELECT claimed_at FROM user_calendar_claims"));

            connection.Execute("CREATE TRIGGER fail_calendar BEFORE INSERT ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='calendar failure'");
            Assert.Throws<MySqlException>(() => service.Claim(7, offer.Campaign, reward, 5, false, now, 16, 28, 40, "TEST"));
            Assert.Equal(new[] { 6 }, service.Opened(7, 1));
            Assert.Equal(13, connection.ExecuteScalar<int>("SELECT credits FROM users WHERE id=7"));
            Assert.Equal(now.AddDays(2), connection.QuerySingle<DateTimeOffset>("SELECT expires_at FROM user_club_memberships WHERE user_id=7"));
            connection.Execute("DROP TRIGGER fail_calendar");
            Assert.NotNull(service.Claim(7, offer.Campaign, reward, 5, false, now, 16, 28, 40, "TEST"));
            Assert.Equal(now.AddDays(4), connection.QuerySingle<DateTimeOffset>("SELECT expires_at FROM user_club_memberships WHERE user_id=7"));
        }

        [CampaignCalendarDatabaseFact]
        public async Task ConcurrentClaimsAndConfigurationChangesCannotConsumeOrAwardTwice()
        {
            using var fixture = new Database();
            var now = CampaignCalendarTests.Now;
            var store = new CampaignCalendarStore(fixture);
            var offer = Assert.IsType<CalendarOffer>(store.Find("configured", now));
            var reward = Assert.Single(offer.Rewards);
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.Claim(7, offer.Campaign, reward, 6, false, now, 13, 24, 35, "TEST"))));
            Assert.Single(results.Where(result => result != null));
            Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items"));
            fixture.Connection.Execute("UPDATE campaign_calendar_rewards SET credits=9 WHERE id=2");
            Assert.Null(store.Claim(7, offer.Campaign, reward, 5, false, now, 22, 28, 40, "TEST"));
            fixture.Connection.Execute("UPDATE campaign_calendar_rewards SET credits=3 WHERE id=2");
            Assert.Null(store.Claim(7, offer.Campaign, reward, 3, false, now, 16, 28, 40, "TEST"));
            Assert.Null(store.Claim(7, offer.Campaign, reward, 24, true, now, 16, 28, 40, "TEST"));
            Assert.NotNull(store.Claim(7, offer.Campaign, reward, 3, true, now, 16, 28, 40, "TEST"));
            Assert.Equal(new[] { 3, 6 }, store.Opened(7, 1));
        }

        [CampaignCalendarDatabaseFact]
        public async Task ActualServiceHandlersPublishOnlyCommittedNativeBundleAndReloggedCalendar()
        {
            using var fixture = new Database();
            var store = new CampaignCalendarStore(fixture);
            var user = CampaignCalendarTests.User();
            var (client, sent) = HabbiconTestSupport.Client(user);
            user.Client = client;
            var service = CampaignCalendarTests.Service(store);
            await new Plus.Communication.Packets.Incoming.Campaign.OpenCampaignCalendarDoorEvent(service)
                .Parse(client, HabbiconTestSupport.Incoming("configured", 6));
            Assert.Equal((13, 24, 35), (user.Credits, user.Duckets, user.Diamonds));
            Assert.True(user.Inventory.Badges.HasBadge("TEST"));
            Assert.Single(user.Inventory.Furniture.AllItems);
            Assert.Equal(1, sent[^1].Payload[0]);
            sent.Clear();
            service.Present(client);
            Assert.Equal(Plus.Communication.Packets.Outgoing.ServerPacketHeader.CampaignCalendarDataComposer, Assert.Single(sent).Header);
            fixture.Connection.Execute("CREATE TRIGGER fail_calendar BEFORE INSERT ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='calendar failure'");
            service.Open(client, "configured", 5);
            Assert.Equal(0, sent[^1].Payload[0]);
            Assert.Equal((13, 24, 35), (user.Credits, user.Duckets, user.Diamonds));
            Assert.Single(user.Inventory.Furniture.AllItems);
            Assert.Equal(new[] { 6 }, store.Opened(7, 1));
        }

        private sealed class Database : IDatabase, IDisposable
        {
            private readonly MySqlConnectionStringBuilder _options;
            private readonly string _schema = "task_calendar_" + Guid.NewGuid().ToString("N");
            public MySqlConnection Connection { get; }
            public Database()
            {
                SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
                _options = new(Environment.GetEnvironmentVariable("CAMPAIGN_CALENDAR_DATABASE")) { Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true };
                Connection = new(_options.ConnectionString);
                Connection.Open();
                Connection.Execute($"CREATE DATABASE `{_schema}`");
                Connection.Execute($"USE `{_schema}`");
                _options.Database = _schema;

                try {
                    var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));

                    foreach (var table in new[] { "users", "furniture", "items", "user_badges", "user_club_memberships", "club_membership_intervals" }) {
                        var start = pristine.IndexOf($"CREATE TABLE `{table}` (", StringComparison.Ordinal);

                        if (start < 0) {
                            start = pristine.IndexOf($"CREATE TABLE IF NOT EXISTS {table} (", StringComparison.Ordinal);
                        }

                        if (start < 0) {
                            start = pristine.IndexOf($"CREATE TABLE {table} (", StringComparison.Ordinal);
                        }

                        Assert.True(start >= 0, table);
                        Connection.Execute(pristine[start..(pristine.IndexOf(';', start) + 1)]);
                    }

                    Connection.Execute("ALTER TABLE user_club_memberships MODIFY expires_at DATETIME(6) NULL DEFAULT NULL, ADD started_at DATETIME(6) NULL, ADD first_started_at DATETIME(6) NULL, ADD past_seconds BIGINT NOT NULL DEFAULT 0, ADD modified_at DATETIME(6) NULL, ADD gifts_claimed INT NOT NULL DEFAULT 0");
                    Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/53_CampaignCalendar.sql")));
                    Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Updates/53_CampaignCalendar.sql")));
                    Connection.Execute("INSERT INTO users(id,username,auth_ticket,credits,activity_points,vip_points) VALUES(7,'calendar','',10,20,30)");
                    Connection.Execute("INSERT INTO furniture(id,item_name,public_name,type) VALUES(10,'reward_furni','Reward','s')");
                    Connection.Execute("INSERT INTO campaign_calendars(id,name,image,starts_at,days,enabled) VALUES(1,'configured','calendar.png',@start,24,TRUE)", new { start = CampaignCalendarTests.Now.AddDays(-6).UtcDateTime });
                    Connection.Execute("INSERT INTO campaign_calendar_rewards(id,campaign_id,product_name,custom_image,credits,duckets,diamonds,badge,item_id,hc_days) VALUES(2,1,'calendar_product_%credits%','reward.png',3,4,5,'TEST',10,2)");
                }
                catch {
                    Dispose();
                    throw;
                }
            }
            IDbConnection IDatabase.Connection() => new MySqlConnection(_options.ConnectionString);
            public bool IsConnected() => true;
            public void Dispose()
            {
                Connection.Execute("USE information_schema");
                Connection.Execute($"DROP DATABASE IF EXISTS `{_schema}`");
                Connection.Dispose();
            }
        }
    }
}
