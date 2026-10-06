using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public class WiredScoreQuotaContractTests
{
    [Fact]
    public void OldConfigurationsRemainUnlimitedAndTypedQuotaRoundTripsWithoutChangingEditorFields()
    {
        var old = JsonSerializer.Deserialize<WiredConfiguration>("{\"IntParams\":[1,4,0]}")!;
        Assert.Null(old.ScoreQuotaPerGame);
        Assert.True(WiredLegacyProtocol.IsWithinLimits(old));
        var proposed = old with
        {
            ScoreQuotaPerGame = 7
        };
        var saved = JsonSerializer.Deserialize<WiredConfiguration>(JsonSerializer.Serialize(proposed))!;
        Assert.Equal(7, saved.ScoreQuotaPerGame);
        Assert.Equal(new[] { 1, 4, 0 }, saved.IntParams);
        Assert.True(WiredLegacyProtocol.IsWithinLimits(saved));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void ExplicitQuotaUsesBoundedDomainValues(int quota, bool valid) =>
        Assert.Equal(valid, WiredLegacyProtocol.IsWithinLimits(new()
        {
            ScoreQuotaPerGame = quota
        }));
}
