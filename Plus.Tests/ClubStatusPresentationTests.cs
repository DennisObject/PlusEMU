using System.Data;
using Plus.Communication.Packets;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Users;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ClubStatusPresentationTests
{
    [Fact]
    public async Task HandlersOnlyDecodePrimitivesAndReturnTheServiceTask()
    {
        var service = new RecordingService();
        var statusTask = new ScrGetUserInfoEvent(service).Parse(null!, HabbiconTestSupport.Incoming("habbo_club"));
        var kickbackTask = new GetKickbackInfoEvent(service).Parse(null!, HabbiconTestSupport.Incoming());

        Assert.Same(service.Pending.Task, statusTask);
        Assert.Same(service.Pending.Task, kickbackTask);
        Assert.Equal(new[] { "habbo_club" }, service.Types);
        Assert.Equal(1, service.KickbackRequests);
        service.Pending.SetResult();
        await Task.WhenAll(statusTask, kickbackTask);
    }

    [Fact]
    public async Task TruncatedStatusFrameNeverCallsTheService()
    {
        var service = new RecordingService();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new ScrGetUserInfoEvent(service).Parse(null!, HabbiconTestSupport.Incoming()));
        Assert.Empty(service.Types);
    }

    [Fact]
    public void CapturedStatusReadsTheClockOnceAndSurvivesExpiryAndAccessReplacement()
    {
        var clock = new Clock();
        var now = clock.Now;
        var membership = new ClubMembership(now.AddDays(32), now.AddDays(-5), now.AddDays(-40),
            35 * ClubMembership.Day, now.AddSeconds(-120));
        var access = UserAccess.Create([], clock: clock, membership: membership);
        var before = clock.Reads;
        var composer = new ScrSendUserInfoComposer(ClubStatusSnapshot.Capture(access, ClubStatusSnapshot.PurchaseResponse));
        Assert.Equal(1, clock.Reads - before);
        var expected = new object[] { "habbo_club", 1, 1, 1, 2, true, false, 40, 0, 46080, 2 };
        Assert.Equal(expected, Write(composer));

        clock.Now = now.AddDays(33);
        access.ReplaceWith(UserAccess.Create([], clock: clock));
        var afterReplacement = clock.Reads;
        Assert.Equal(expected, Write(composer));
        Assert.Equal(expected, Write(composer));
        Assert.Equal(afterReplacement, clock.Reads);
        Assert.Equal(0, ClubStatusSnapshot.Capture(access).MinutesLeft);
    }

    [Fact]
    public void UnknownAndVeryLongTenureKeepTheExistingWireDefaultsAndClamps()
    {
        Assert.Equal(new object[] { "habbo_club", 0, 0, 0, 3, false, false, 0, 0, 0, 0 },
            Write(new ScrSendUserInfoComposer(ClubStatusSnapshot.Capture(ClubMembership.None,
                DateTimeOffset.MaxValue, ClubStatusSnapshot.ExpiringResponse))));
        var membership = new ClubMembership(DateTimeOffset.MaxValue, pastSeconds: long.MaxValue,
            modifiedAt: DateTimeOffset.MinValue);
        var status = ClubStatusSnapshot.Capture(membership, DateTimeOffset.MinValue);
        Assert.Equal(int.MaxValue, status.PeriodsElapsed);
        Assert.Equal(int.MaxValue, status.ElapsedDays);
        Assert.Equal(int.MaxValue, status.MinutesLeft);
        Assert.Equal(0, status.MinutesSinceModified);
    }

    [Fact]
    public async Task StatusServiceKeepsExactTypeGateAndMembershipPayload()
    {
        var clock = new Clock();
        var now = clock.Now;
        var habbo = new Habbo
        {
            Access = UserAccess.Create([], clock: clock, membership: new(now.AddDays(32), now.AddDays(-5),
                now.AddDays(-40), 35 * ClubMembership.Day, now.AddSeconds(-120)))
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var service = new ClubCatalogService(new Rewards(), null!, null!, null!);
        await service.ShowStatus(client, "habbo_club_other");
        await service.ShowStatus(client, "HABBO_CLUB");
        Assert.Empty(sent);
        var before = clock.Reads;

        await service.ShowStatus(client, "habbo_club");

        Assert.Equal(1, clock.Reads - before);
        var result = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.ScrSendUserInfoComposer, result.Header);
        var packet = new FlashIncomingPacket { Buffer = result.Payload };
        Assert.Equal("habbo_club", packet.ReadString());
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(1, packet.ReadInt());
        Assert.True(packet.ReadBool());
        Assert.False(packet.ReadBool());
        Assert.Equal(40, packet.ReadInt());
        Assert.Equal(0, packet.ReadInt());
        Assert.Equal(46080, packet.ReadInt());
        Assert.Equal(2, packet.ReadInt());
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public async Task KickbackIsPreparedBeforeTheSingleExactResponse()
    {
        var habbo = new Habbo { Id = 42 };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var reads = 0;
        var rewards = new Rewards
        {
            ReadKickback = target =>
            {
                Assert.Same(habbo, target);
                Assert.Empty(sent);
                reads++;

                return new(7, "01-10-2040", .25, 2, 3, 40, 5, 10, 60);
            }
        };
        var service = new ClubCatalogService(rewards, null!, null!, null!);

        await service.ShowKickback(client);

        Assert.Equal(1, reads);
        var result = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.KickbackInfoComposer, result.Header);
        var packet = new FlashIncomingPacket { Buffer = result.Payload };
        Assert.Equal(7, packet.ReadInt());
        Assert.Equal("01-10-2040", packet.ReadString());
        var percentage = new byte[sizeof(double)];
        packet.ReadBytes(percentage);
        Assert.Equal(.25, System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(percentage));
        Assert.Equal(new[] { 2, 3, 40, 5, 10, 60 }, Enumerable.Range(0, 6).Select(_ => packet.ReadInt()));
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public async Task KickbackReadFailurePublishesNothing()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        var rewards = new Rewards { ReadKickback = _ => throw new InvalidOperationException("lookup failed") };
        var service = new ClubCatalogService(rewards, null!, null!, null!);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ShowKickback(client));

        Assert.Empty(sent);
    }

    private static object[] Write(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);

        return packet.Writes.ToArray();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2040, 10, 1, 0, 0, 0, TimeSpan.FromHours(5));
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return Now;
        }
    }

    private sealed class RecordingService : IClubCatalogService
    {
        public TaskCompletionSource Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Types { get; } = [];
        public int KickbackRequests { get; private set; }
        public Task ShowStatus(GameClient session, string type)
        {
            Types.Add(type);

            return Pending.Task;
        }
        public Task ShowKickback(GameClient session)
        {
            KickbackRequests++;

            return Pending.Task;
        }
        public Task ShowGifts(GameClient session) => throw new NotSupportedException();
        public Task ClaimGift(GameClient session, string productCode) => throw new NotSupportedException();
        public Task PurchaseMembership(GameClient session, int offerId) => throw new NotSupportedException();
    }

    private sealed class Rewards : IClubRewards
    {
        public Func<Habbo, ClubKickback> ReadKickback { get; init; } = _ => throw new NotSupportedException();
        public ClubKickback Kickback(Habbo habbo) => ReadKickback(habbo);
        public ClubGiftInfo Gifts(Habbo habbo) => throw new NotSupportedException();
        public ClubGiftClaim? Claim(Habbo habbo, string productCode) => throw new NotSupportedException();
        public bool Charge(Habbo habbo, int credits, int duckets = 0, int diamonds = 0,
            Func<IDbConnection, IDbTransaction, bool>? deliver = null, bool kickbackEligible = true) => throw new NotSupportedException();
        public void RunPaydays() => throw new NotSupportedException();
    }
}
