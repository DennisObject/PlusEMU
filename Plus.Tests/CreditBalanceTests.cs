using Plus.HabboHotel.Catalog.Marketplace;
using Xunit;

namespace Plus.Tests;

public class CreditBalanceTests
{
    [Fact]
    public void AddToBalanceRejectsOverflowAndNegativeTotals()
    {
        Assert.Equal(150, CreditBalance.AddToBalance(100, 50));
        Assert.Equal(int.MaxValue, CreditBalance.AddToBalance(int.MaxValue - 1, 1));
        Assert.Null(CreditBalance.AddToBalance(int.MaxValue, 1));
        Assert.Null(CreditBalance.AddToBalance(-5, 1));
    }
}
