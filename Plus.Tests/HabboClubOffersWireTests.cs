using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Xunit;

namespace Plus.Tests;

public class HabboClubOffersWireTests
{
    [Fact]
    public void ClubOffersFollowTheClientOfferLayoutAndEndWithTheWindowId()
    {
        var offer = new ClubOffer { Id = 2, Name = "HABBO_CLUB_3_MONTHS", Days = 95, Credits = 250, Points = 10, PointsType = 5 };
        var membershipEnd = DateTime.UtcNow.Date.AddDays(10);
        var endsAt = membershipEnd.AddDays(95);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new HabboClubOffersComposer([offer], 1, membershipEnd).Compose(packet);

        var daysLeft = (int)Math.Ceiling((endsAt - DateTime.UtcNow).TotalDays);
        Assert.Equal(new List<object>
        {
            1,
            2, "HABBO_CLUB_3_MONTHS", false, 250, 10, 5, true, 3, 2, false, daysLeft, endsAt.Year, endsAt.Month, endsAt.Day,
            1
        }, packet.Writes);
    }
}
