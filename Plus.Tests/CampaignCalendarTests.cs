using System.Collections.Immutable;
using Plus.Communication.Packets.Incoming.Campaign;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Campaign;
using Plus.HabboHotel.Campaigns;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests
{
    public sealed class CampaignCalendarTests
    {
        internal static readonly DateTimeOffset Now = new(2026, 12, 7, 12, 0, 0, TimeSpan.Zero);
        internal static CalendarCampaign Campaign => new(1, "configured", "calendar.png", Now.AddDays(-6), 24, true, 2);
        internal static CalendarReward Reward => new(2, 1, "calendar_product_%credits%", "reward.png", 3, 4, 5, "TEST", 10, 0);

        [Fact]
        public void NativeDataAndResultFieldOrderAndUtcWindowsArePreserved()
        {
            var campaign = Campaign;
            var data = campaign.Capture(Now.ToOffset(TimeSpan.FromHours(5.5)), [4, 2, 4, -1, 100]);
            var packet = new HabbiconTestSupport.RecordingPacket();
            new CampaignCalendarDataComposer(data).Compose(packet);
            Assert.Equal(new object[] { "configured", "calendar.png", 6, 24, 2, 2, 4, 3, 0, 1, 3 }, packet.Writes);
            var result = new HabbiconTestSupport.RecordingPacket();
            new CampaignCalendarDoorOpenedComposer(true, "product", "image", "furni").Compose(result);
            Assert.Equal(new object[] { true, "product", "image", "furni" }, result.Writes);
            Assert.False(campaign.CanOpen(Now, -1, true));
            Assert.False(campaign.CanOpen(Now, 24, true));
            Assert.False(campaign.CanOpen(Now, 7, false));
            Assert.True(campaign.CanOpen(Now, 7, true));
            Assert.True(campaign.CanOpen(Now, 4, false));
            Assert.False(campaign.CanOpen(Now, 3, false));
            Assert.True((campaign with { LockExpired = false }).CanOpen(Now, 0, false));
            Assert.False(campaign.CanOpen(campaign.StartsAt.AddTicks(-1), 0, true));
            Assert.False(campaign.CanOpen(campaign.StartsAt.AddDays(24), 23, true));
        }

        [Fact]
        public async Task ActualHandlersDecodeCampaignAndDayAndGrantOnceBeforeAnyPublication()
        {
            var user = User();
            var (client, sent) = HabbiconTestSupport.Client(user);
            user.Client = client;
            var store = new Store();
            var service = Service(store);
            service.Present(client);
            Assert.Equal(ServerPacketHeader.CampaignCalendarDataComposer, Assert.Single(sent).Header);
            sent.Clear();
            var original = client.SendCallback;
            client.SendCallback = args =>
            {
                Assert.Equal((13, 24, 35), (user.Credits, user.Duckets, user.Diamonds));
                Assert.True(user.Inventory.Badges.HasBadge("TEST"));
                Assert.True(user.Inventory.Furniture.HasItem(99));
                Assert.Equal(1, store.Writes);

                return original(args);
            };
            await new OpenCampaignCalendarDoorEvent(service).Parse(client, HabbiconTestSupport.Incoming("configured", 6));
            Assert.Equal(6, store.Day);
            Assert.Equal("configured", store.Name);
            Assert.Equal(ServerPacketHeader.CampaignCalendarDoorOpenedComposer, sent[^1].Header);
            Assert.Equal(1, sent[^1].Payload[0]);
            service.Open(client, "configured", 6);
            Assert.Equal(1, store.Writes);
            Assert.Equal(0, sent[^1].Payload[0]);
        }

        [Fact]
        public void CalendarLoadedForADepartedSessionIsNotPublished()
        {
            var user = User();
            var (client, sent) = HabbiconTestSupport.Client(user);
            user.Client = client;
            var store = new Store { OnOpened = user.Dispose };
            Service(store).Present(client);
            Assert.Empty(sent);
            Assert.Equal(0, store.Writes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StaffForceRequiresTheExistingPermissionAndKeepsCampaignBounds(bool allowed)
        {
            var user = User();
            user.Access = UserAccess.Create([], allowed ? [new UserPermissionOverride(PermissionKeys.CampaignCalendarForce, false)] : []);
            var (client, sent) = HabbiconTestSupport.Client(user);
            user.Client = client;
            var store = new Store();
            await new OpenCampaignCalendarDoorAsStaffEvent(Service(store)).Parse(client, HabbiconTestSupport.Incoming("configured", 23));
            Assert.Equal(allowed ? 1 : 0, store.Writes);
            Assert.Equal(allowed, store.Staff);
            Assert.Equal(allowed ? 1 : 0, sent.Count(packet => packet.Header == ServerPacketHeader.CampaignCalendarDoorOpenedComposer));
        }

        [Theory]
        [InlineData("write")]
        [InlineData("badge")]
        [InlineData("item")]
        [InlineData("overflow")]
        public void FailureLeavesClaimLiveRewardAndInventoryUnchanged(string failure)
        {
            var user = User();
            var (client, sent) = HabbiconTestSupport.Client(user);
            user.Client = client;
            var store = new Store { Fail = failure == "write" };

            if (failure == "badge") {
                store.Prize = Reward with { Badge = "missing" };
            }

            if (failure == "item") {
                store.Prize = Reward with { ItemId = 1234 };
            }

            if (failure == "overflow") {
                user.Credits = int.MaxValue;
            }

            var old = (user.Credits, user.Duckets, user.Diamonds);
            Service(store).Open(client, "configured", 6);
            Assert.Equal(old, (user.Credits, user.Duckets, user.Diamonds));
            Assert.Equal(0, store.Writes);
            Assert.Empty(user.Inventory.Badges.Badges);
            Assert.Empty(user.Inventory.Furniture.AllItems);
            Assert.Equal(ServerPacketHeader.CampaignCalendarDoorOpenedComposer, Assert.Single(sent).Header);
            Assert.Equal(0, sent[0].Payload[0]);
        }

        [Fact]
        public void ForeignDetachedAndClosedSessionsNeverClaimAndClubDucketsUseTheCapturedClock()
        {
            var user = User();
            var (client, sent) = HabbiconTestSupport.Client(user);
            var store = new Store();
            Service(store).Open(client, "configured", 6);
            Assert.Equal(0, store.Writes);
            user.Client = client;
            user.Access = UserAccess.Create([], clock: new FixedTimeProvider(Now), membership: new ClubMembership(Now.AddDays(1), Now));
            Service(store).Open(client, "configured", 6);
            Assert.Equal(28, user.Duckets);
            Assert.Equal(1, store.Writes);
            user.Dispose();
            sent.Clear();
            Service(store).Open(client, "configured", 5);
            Assert.Equal(1, store.Writes);
            Assert.Empty(sent);
        }

        internal static Habbo User() => new()
        {
            Id = 7,
            Credits = 10,
            Duckets = 20,
            Diamonds = 30,
            Access = UserAccess.Empty,
            Inventory = new InventoryComponent { Badges = new([]), Furniture = new([], []) }
        };

        internal static CampaignCalendarService Service(ICampaignCalendarStore store) => new(store,
            CatalogSnapshotTestSupport.Proxy<IItemDataManager>((method, _) => method == "get_Items"
                ? new Dictionary<uint, ItemDefinition> { [10] = new() { Id = 10, ItemName = "reward_furni", ProductType = "s" } }
                : throw new NotSupportedException(method)),
            CatalogSnapshotTestSupport.Proxy<IBadgeManager>((method, _) => method == "get_Badges"
                ? new Dictionary<string, BadgeDefinition> { ["TEST"] = new() { Code = "TEST" } } : throw new NotSupportedException(method)),
            CatalogSnapshotTestSupport.Proxy<IAccessControl>((method, _) => method == "Refresh" ? null : throw new NotSupportedException(method)),
            new AccountSessionGate(), new FixedTimeProvider(Now), TestLogging.For<CampaignCalendarService>());

        private sealed class Store : ICampaignCalendarStore
        {
            public CalendarReward Prize = Reward;
            public bool Fail;
            public Action? OnOpened;
            public int Writes;
            public int Day;
            public string Name = "";
            public bool Staff;
            private readonly HashSet<int> _days = new();
            public CalendarOffer? Find(string? name, DateTimeOffset now) => new(Campaign, [Prize]);
            public IReadOnlyList<int> Opened(int userId, int campaignId)
            {
                OnOpened?.Invoke();

                return _days.ToArray();
            }
            public CalendarGrant? Claim(int userId, CalendarCampaign campaign, CalendarReward reward, int day, bool staff,
                DateTimeOffset now, int credits, int duckets, int diamonds, string badge)
            {
                if (Fail) {
                    throw new InvalidOperationException("failed transaction");
                }

                if (!_days.Add(day)) {
                    return null;
                }

                Writes++;
                Day = day;
                Name = campaign.Name;
                Staff = staff;

                return new(99, credits, duckets, diamonds, badge, reward.ClubDays);
            }
        }
    }
}
