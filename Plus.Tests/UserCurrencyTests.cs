using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class UserCurrencyTests
{
    [Fact]
    public void HabboBalancesAreActivityPointTypes()
    {
        var habbo = new Habbo { Duckets = 1, Diamonds = 2, GotwPoints = 3 };
        habbo.Currencies[101] = 4;

        Assert.Equal((1, 2, 3, 4), (habbo.Currencies[0], habbo.Currencies[5], habbo.Currencies[103], habbo.Currencies[101]));
        Assert.Equal(0, habbo.Currencies[104]);
        Assert.Throws<ArgumentOutOfRangeException>(() => habbo.Currencies[-1] = 1);
    }

    [Fact]
    public void PurseListsEveryHeldTypeAndAlwaysDucketsDiamondsAndGotw()
    {
        var currencies = new UserCurrencies();
        currencies.Load([new(104, 7), new(5, 9)]);
        var packet = new HabbiconTestSupport.RecordingPacket();

        new ActivityPointsComposer(currencies).Compose(packet);

        Assert.Equal(new object[] { 4, 0, 0, 5, 9, 103, 0, 104, 7 }, packet.Writes);
    }

    [Fact]
    public void LoadReplacesTheBalances()
    {
        var currencies = new UserCurrencies { [101] = 5 };
        currencies.Load([new(0, 3)]);

        Assert.Equal([new KeyValuePair<int, int>(0, 3)], currencies.Snapshot());
    }
}
