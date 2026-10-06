using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Incoming.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public class HabboClubOffersWireTests
{
    private static readonly DateTimeOffset Now = new(2040, 12, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClubOffersFollowTheClientOfferLayoutAndEndWithTheWindowId()
    {
        var offer = Offer();
        var end = Now.AddDays(10);
        var endsAt = end.AddDays(95);
        var packet = new HabbiconTestSupport.RecordingPacket();
        var snapshot = ClubOfferSnapshotFactory.Capture([offer], 1, end, Now);

        new HabboClubOffersComposer(snapshot).Compose(packet);

        Assert.Equal(new List<object>
        {
            1, 2, "HABBO_CLUB_3_MONTHS", false, 250, 10, 5, true, 3, 2, false,
            105, endsAt.Year, endsAt.Month, endsAt.Day, 1
        }, packet.Writes);
    }

    [Fact]
    public void OfferAndExtensionSnapshotsIgnoreSourceMutation()
    {
        var offer = Offer();
        var source = new List<ClubOffer> { offer };
        var snapshot = ClubOfferSnapshotFactory.Capture(source, 1, Now.AddDays(10), Now);
        var extension = new ClubExtensionSnapshot(snapshot.Offers[0], 83, 3, 10);
        var first = new HabbiconTestSupport.RecordingPacket();
        new HabboClubOffersComposer(snapshot).Compose(first);
        var extendedFirst = new HabbiconTestSupport.RecordingPacket();
        new HabboClubExtendOfferComposer(extension).Compose(extendedFirst);

        offer.Name = "changed";
        offer.Days = 1;
        offer.Credits = 99;
        source.Clear();
        var second = new HabbiconTestSupport.RecordingPacket();
        new HabboClubOffersComposer(snapshot).Compose(second);
        var extendedSecond = new HabbiconTestSupport.RecordingPacket();
        new HabboClubExtendOfferComposer(extension).Compose(extendedSecond);

        Assert.Equal(first.Writes, second.Writes);
        Assert.Equal(extendedFirst.Writes, extendedSecond.Writes);
        Assert.Equal(new object[] { 83, 3, 5, 10 }, extendedSecond.Writes.TakeLast(4));
    }

    [Fact]
    public void ExpiredMembershipStartsTheOfferAtTheCapturedCurrentTime()
    {
        var snapshot = ClubOfferSnapshotFactory.Capture([Offer()], 1, Now.AddDays(-10), Now);
        Assert.Equal(95, snapshot.Offers[0].DaysLeft);
        Assert.Equal(Now.AddDays(95).Day, snapshot.Offers[0].EndDay);
    }

    [Fact]
    public async Task HandlersOnlyDecodeAndDelegate()
    {
        var service = new RecordingService();
        await new GetHabboClubWindowEvent(service).Parse(null!, HabbiconTestSupport.Incoming(42));
        await new GetHabboClubExtendOfferEvent(service).Parse(null!, HabbiconTestSupport.Incoming());
        Assert.Equal(42, service.Window);
        Assert.True(service.Extended);
    }

    private static ClubOffer Offer() => new()
    {
        Id = 2,
        Name = "HABBO_CLUB_3_MONTHS",
        Days = 95,
        Credits = 250,
        Points = 10,
        PointsType = 5
    };

    private sealed class RecordingService : IClubOfferSnapshotService
    {
        public int Window { get; private set; }
        public bool Extended { get; private set; }
        public void ShowOffers(GameClient session, int windowId) => Window = windowId;
        public void ShowExtension(GameClient session) => Extended = true;
    }
}
