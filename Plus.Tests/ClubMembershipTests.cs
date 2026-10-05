using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Core.FigureData;
using Plus.Core.FigureData.Types;
using Plus.Core.Settings;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.Communication.Packets.Outgoing.Users;
using Xunit;

namespace Plus.Tests;

public class ClubMembershipTests
{
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        private TimerCallback? _callback;
        private object? _state;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback; _state = state;
            return new ManualTimer();
        }
        public void Tick() => _callback!(_state);
        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    [Fact]
    public void AbsentActiveAndExactExpiryUseOneSnapshotWithoutDatabaseReads()
    {
        var clock = new Clock();
        var now = clock.Now;
        var access = UserAccess.Create([], clock: clock, membership: new(now.AddSeconds(60), now, now));
        Assert.Equal(0, ClubAccess.LevelFor(UserAccess.Empty));
        Assert.Equal(2, ClubAccess.LevelFor(access));
        var style = new ChatStyle(1, "HC", "", true);
        var model = new RoomModel("club", 0, 0, 0, 0, "00\r00", 2, 0, false);
        var item = new CatalogItem { ClubLevel = 2 };
        var habbo = new Habbo { Access = access };
        Assert.True(style.CanUse(access)); Assert.True(model.CanCreate(access)); Assert.True(item.CanPurchase(habbo));
        clock.Now = clock.Now.AddSeconds(60);
        Assert.Equal(0, ClubAccess.LevelFor(access));
        Assert.False(style.CanUse(access)); Assert.False(model.CanCreate(access)); Assert.False(item.CanPurchase(habbo));
        var rights = new HabbiconTestSupport.RecordingPacket();
        new UserRightsComposer(UserRightsSnapshot.Capture(access)).Compose(rights);
        Assert.Equal(0, rights.Writes[0]);
    }
    [Fact]
    public void StaffNeedTheClubAccessPermissionAndDenyWins()
    {
        var staff = new AccessRole(8, "staff", "Staff", 80, 7, "", true, [], new Dictionary<string, int>());
        Assert.Equal(0, ClubAccess.LevelFor(UserAccess.Create([new(staff)])));
        Assert.Equal(2, ClubAccess.LevelFor(UserAccess.Create([new(staff)], [new(PermissionKeys.ClubAccess, false)])));
        Assert.Equal(0, ClubAccess.LevelFor(UserAccess.Create([new(staff)], [new("*", false), new(PermissionKeys.ClubAccess, true)])));
        var vip = staff with { Slug = "vip", IsStaff = false };
        Assert.Equal(0, ClubAccess.LevelFor(UserAccess.Create([new(vip)])));
    }
    [Fact]
    public void ExtensionUsesCurrentExpiryOrNowAndDoesNotOverflowIntMultiplication()
    {
        var now = At(1000);
        Assert.Equal(now.AddDays(31), ClubMembership.Extend(now, At(999), 31));
        Assert.Equal(At(2000).AddDays(31), ClubMembership.Extend(now, At(2000), 31));
        Assert.Equal(now.AddDays(36500), ClubMembership.Extend(now, null, 36500));
        // A representable range is refused instead of overflowing the calendar.
        Assert.Null(ClubMembership.Extend(now, null, int.MaxValue));
    }
    [Fact]
    public void StatusCarriesRealTimeHistoryAndHcBranding()
    {
        var clock = new Clock(); var now = clock.Now;
        var membership = new ClubMembership(now.AddDays(32), now.AddDays(-5), now.AddDays(-40), 35 * ClubMembership.Day, now.AddSeconds(-120));
        var packet = new HabbiconTestSupport.RecordingPacket();
        new ScrSendUserInfoComposer(UserAccess.Create([], clock: clock, membership: membership), ScrSendUserInfoComposer.PurchaseResponse).Compose(packet);
        Assert.Equal(new object[] { "habbo_club", 1, 1, 1, 2, true, false, 40, 0, 46080, 2 }, packet.Writes);
        var complimentary = new HabbiconTestSupport.RecordingPacket();
        new ScrSendUserInfoComposer(UserAccess.Create([], [new(PermissionKeys.ClubAccess, false)], clock: clock)).Compose(complimentary);
        Assert.Equal(new object[] { "habbo_club", 0, 0, 0, 1, false, false, 0, 0, 0, 0 }, complimentary.Writes);
        var absent = new HabbiconTestSupport.RecordingPacket();
        new ScrSendUserInfoComposer(UserAccess.Create([], clock: clock)).Compose(absent);
        Assert.Equal(new object[] { "habbo_club", 0, 0, 0, 1, false, false, 0, 0, 0, 0 }, absent.Writes);
    }
    [Fact]
    public void GiftsEarnFromElapsedTenureAndNotPrepaidTime()
    {
        var membership = new ClubMembership(At(100).AddDays(93), At(100), At(100));
        Assert.Equal(0, membership.AvailableGifts(At(100)));
        Assert.Equal(1, membership.AvailableGifts(At(101)));
        Assert.Equal(1, membership.AvailableGifts(At(100 + ClubMembership.Period)));
        Assert.Equal(2, membership.AvailableGifts(At(101 + ClubMembership.Period)));
        Assert.Equal(0, (membership with { GiftsClaimed = 1 }).AvailableGifts(At(101)));
    }
    [Theory]
    [InlineData(6, 0)] [InlineData(7, 5)] [InlineData(30, 10)] [InlineData(365, 30)]
    public void KickbackUsesPolarisStreakBandsAndFlooredSpending(int days, int bonus)
    {
        Assert.Equal(bonus, ClubRewards.StreakBonus(days));
        Assert.Equal(9, ClubRewards.SpendingBonus(99, .1));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), ClubRewards.NextPayday(new Clock().Now));
    }
    [Fact]
    public void FigureFallbackCannotPickSameGenderPremiumSetsOrPremiumColors()
    {
        var manager = new FigureDataManager(null!, NullLogger<FigureDataManager>.Instance);
        var types = (Dictionary<string, FigureSet>)typeof(FigureDataManager).GetField("_setTypes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        var palettes = (Dictionary<int, Palette>)typeof(FigureDataManager).GetField("_palettes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        var hd = new FigureSet(SetTypeUtility.GetSetType("hd"), 1);
        hd.Sets.Add(10, new(10, "M", 2, true, true, true));
        hd.Sets.Add(11, new(11, "U", 0, true, true, true));
        types.Add("hd", hd);
        var palette = new Palette(1);
        palette.Colors.Add(20, new(20, 1, 2, true, "000000"));
        palette.Colors.Add(21, new(21, 2, 0, true, "FFFFFF"));
        palettes.Add(1, palette);
        Assert.Equal("hd-11-21-21.", manager.ProcessFigure("hd-10-20-20", "M", null!, 0));
        Assert.Equal("hd-10-20-20.", manager.ProcessFigure("hd-10-20-20", "M", null!, 2));
        Assert.Equal("hd-11-21.", manager.ProcessFigure("", "M", null!, 0));
    }
    [Fact]
    public void CatalogPagesOffersAndConfigurableLimitsUseMembershipWithAclOverrides()
    {
        var normal = new Habbo();
        var member = new Habbo { Access = UserAccess.Create([], [new(PermissionKeys.ClubAccess, false)]) };
        var page = new CatalogPage { Enabled = true, RequiredClubLevel = 1 };
        Assert.False(page.CanOpen(normal)); Assert.True(page.CanOpen(member));
        page.RequiredPermission = PermissionKeys.CatalogEdit; Assert.False(page.CanOpen(member));
        var settings = DispatchProxy.Create<ISettingsManager, SettingProxy>();
        Assert.Equal(50, ClubLimits.For(normal.Access, "visitors", settings));
        Assert.Equal(75, ClubLimits.For(member.Access, "visitors", settings));
        ((SettingProxy)settings).Values["club.limit.visitors.member"] = "90";
        Assert.Equal(90, ClubLimits.For(member.Access, "visitors", settings));
        var role = new AccessRole(2, "capacity", "Capacity", 1, 1, "", false, [], new Dictionary<string, int> { ["limit.visitors"] = 120 });
        Assert.Equal(120, ClubLimits.For(UserAccess.Create([new(role)]), "visitors", settings));
    }
    public class SettingProxy : DispatchProxy
    {
        public Dictionary<string, string> Values { get; } = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Values.GetValueOrDefault((string)args![0]!);
    }
    [Fact]
    public void InstantsNormalizeToUtcAndDurationsCountWholeSeconds()
    {
        var local = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(2));
        var membership = new ClubMembership(local.AddDays(1), local, local.AddDays(-1));
        Assert.Equal(TimeSpan.Zero, membership.StartedAt!.Value.Offset);
        Assert.Equal(local.UtcDateTime, membership.StartedAt.Value.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, membership.ExpiresAt!.Value.Offset);
        Assert.Equal(local.ToUniversalTime().AddDays(1), membership.ExpiresAt.Value);

        var fractional = new ClubMembership(At(1_000_000), At(100).AddMilliseconds(500));
        Assert.Equal(0, fractional.Elapsed(At(100).AddMilliseconds(900)));
        Assert.Equal(0, fractional.Elapsed(At(101).AddMilliseconds(400)));
        Assert.Equal(1, fractional.Elapsed(At(101).AddMilliseconds(500)));
        Assert.Equal(1, new ClubMembership(At(1000).AddMilliseconds(700)).SecondsLeft(At(999).AddMilliseconds(400))); // 1.3 s left counts one whole second
        Assert.Equal(0, new ClubMembership(modifiedAt: At(200)).SecondsSinceModified(At(100)));
    }

    [Fact]
    public void ExtensionUsesTheLaterInstantAndRefusesOnlyUnrepresentableResults()
    {
        var nearMax = DateTimeOffset.MaxValue.AddDays(-2);
        Assert.Equal(DateTimeOffset.MaxValue, ClubMembership.Extend(nearMax, null, 2));
        Assert.Null(ClubMembership.Extend(nearMax, null, 3));
        Assert.Null(ClubMembership.Extend(DateTimeOffset.MaxValue, null, 1));
        Assert.Null(ClubMembership.Extend(At(1000), null, -1));
        Assert.Null(ClubMembership.Extend(At(1000), null, int.MaxValue));

        var fractional = At(1000).AddMilliseconds(500);
        Assert.Equal(fractional.AddDays(1), ClubMembership.Extend(fractional, null, 1));
        var zoned = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.FromHours(-5));
        var extended = ClubMembership.Extend(At(1000), zoned, 1)!.Value;
        Assert.Equal(TimeSpan.Zero, extended.Offset);
        Assert.Equal(zoned.AddDays(1), extended);
    }

    private static DateTimeOffset At(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);
}
