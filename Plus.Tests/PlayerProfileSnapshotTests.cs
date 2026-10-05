using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class PlayerProfileSnapshotTests
{
    [Fact]
    public void ProfileWireLayoutUsesCapturedDataAndInvariantDates()
    {
        PlayerProfileSnapshot profile = new(7, "Alice", "hd-1", "hello", new(2020, 2, 3, 0, 0, 0, TimeSpan.Zero),
            12, 3, false, true, false, [new(42, "Group", "badge", "red", "blue", true, true)], 90);
        var composer = new ProfileInformationComposer(profile);
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var packet = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(packet);
            Assert.Equal(new object[] { 7, "Alice", "hd-1", "hello", "03/02/2020", 12, 3, false, true, false, 1,
                42, "Group", "badge", "red", "blue", true, 0, true, 90, true }, packet.Writes);
            var changed = profile with { Groups = profile.Groups.Add(new(99, "New", "", "", "", false, false)) };
            Assert.Equal(2, changed.Groups.Length);
            var repeated = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(repeated);
            Assert.Equal(packet.Writes, repeated.Writes);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(2_200_000_000L, 0)]
    [InlineData(1_700_000_000L, 100)]
    [InlineData(-62_135_596_800L, int.MaxValue)]
    public async Task ProfileServiceCapturesAndBoundsElapsedTime(long? lastOnline, int expected)
    {
        var habbo = new Habbo { Id = 7, Username = "Alice", Look = "hd-1", Motto = "hello",
            LastOnlineAt = lastOnline.HasValue ? DateTimeOffset.FromUnixTimeSeconds(lastOnline.Value) : null };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var clients = new HousekeepingActionTests.FakeClients();
        clients.Online[7] = client;
        var groups = DispatchProxy.Create<IGroupManager, EmptyGroups>();
        var messenger = DispatchProxy.Create<IMessengerDataLoader, FriendCount>();
        var profiles = new PlayerProfileService(groups, messenger, clients, null!, new Stats(), new FixedClock());
        await profiles.Open(client, 7);
        var payload = Assert.Single(sent).Payload;
        Assert.Equal(expected, BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(payload.Length - 5, 4)));
        Assert.Equal((byte)1, payload[^1]);
    }

    [Fact]
    public async Task ProfileElapsedSecondsRetainFractionsUntilTheWireBoundary()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_100).ToOffset(TimeSpan.FromHours(9));
        var habbo = new Habbo { Id = 7, LastOnlineAt = now.ToOffset(TimeSpan.FromHours(-7)).AddTicks(-1) };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var clients = new HousekeepingActionTests.FakeClients();
        clients.Online[7] = client;
        var profiles = new PlayerProfileService(DispatchProxy.Create<IGroupManager, EmptyGroups>(),
            DispatchProxy.Create<IMessengerDataLoader, FriendCount>(), clients, null!, new Stats(), new FixedClock());
        await profiles.Open(client, 7);
        var payload = Assert.Single(sent).Payload;
        Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(payload.Length - 5, 4)));
    }

    [Fact]
    public async Task ProfileElapsedSecondsTruncateTicksJustBelowTheIntegerLimit()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_100).ToOffset(TimeSpan.FromHours(9));
        var habbo = new Habbo { Id = 7, LastOnlineAt = now.ToOffset(TimeSpan.FromHours(-7)).AddSeconds(-int.MaxValue).AddTicks(1) };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var clients = new HousekeepingActionTests.FakeClients();
        clients.Online[7] = client;
        var profiles = new PlayerProfileService(DispatchProxy.Create<IGroupManager, EmptyGroups>(),
            DispatchProxy.Create<IMessengerDataLoader, FriendCount>(), clients, null!, new Stats(), new FixedClock());
        await profiles.Open(client, 7);
        var payload = Assert.Single(sent).Payload;
        Assert.Equal(int.MaxValue - 1, BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(payload.Length - 5, 4)));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeSeconds(1_700_000_100);
    }
    private sealed class Stats : IHabboStatsService
    {
        public Task<HabboStats> LoadHabboStats(int id) => Task.FromResult(new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 12, 0, 0, 0, "", 0));
        public Task UpdateDailyRespectsAndTimestamp(int id, int dailyRespects, string timestamp) => throw new NotSupportedException();
    }
    private class EmptyGroups : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "GetGroupsForUser"
            ? new List<Group>() : throw new NotSupportedException(method?.Name);
    }
    private class FriendCount : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "GetFriendCount"
            ? Task.FromResult(3) : throw new NotSupportedException(method?.Name);
    }
}
