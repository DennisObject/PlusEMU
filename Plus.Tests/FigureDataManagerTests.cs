using Microsoft.Extensions.Logging.Abstractions;
using Plus.Core.FigureData;
using Xunit;

namespace Plus.Tests;

public class FigureDataManagerTests
{
    private static FigureDataManager Manager()
    {
        // Init reads Config/figuredata.xml, Habbo's figuredata, from the working directory (the test output folder).
        var manager = new FigureDataManager(NullLogger<FigureDataManager>.Instance);
        manager.Init();

        return manager;
    }

    [Theory]
    [InlineData("hd-180-1.ha-1018")]
    [InlineData("hd-180-1.ca-1813")]
    [InlineData("hd-180-1.wa-2001")]
    [InlineData("hd-180-1.ca-1813-")]
    [InlineData("hd-180-1.ca-1813-x")]
    [InlineData("hd-180-1.hr")]
    [InlineData("hd-180-1.hr-")]
    [InlineData("hd-180-1.hr-abc-45")]
    [InlineData("hd-180-1..ch-215-66")]
    public void MalformedPartsAreDroppedOrNormalisedInsteadOfThrowing(string figure)
    {
        var processed = Manager().ProcessFigure(figure, "M", null!, 2);

        Assert.Contains("hd-180-", processed);
        Assert.All(processed.TrimEnd('.').Split('.'), part => Assert.Matches(@"^[a-z]{2}-\d+-\d+(-\d+)?$", part));
    }

    [Fact]
    public void WellFormedFiguresKeepTheirParts()
    {
        var processed = Manager().ProcessFigure("hd-180-1.ch-215-66.lg-270-82.sh-290-80", "M", null!, 2);

        Assert.Contains("hd-180-1.", processed);
        Assert.Contains("ch-215-66.", processed);
    }

    [Fact]
    public void SellableSetsNeedTheirClothingRedeemed()
    {
        // ch-3438 and mc-3360 are sellable on Habbo; ch-215 is not.
        const string Look = "hd-180-1.ch-3438-66.lg-270-82.mc-3360-66";
        var unowned = Manager().ProcessFigure(Look, "M", [], 0);
        var owned = Manager().ProcessFigure(Look, "M", [new(1, 3438, "clothing_a"), new(2, 3360, "clothing_b")], 0);

        Assert.DoesNotContain("ch-3438-", unowned);
        Assert.DoesNotContain("mc-3360-", unowned);
        Assert.Contains("ch-3438-66.", owned);
        Assert.Contains("mc-3360-66.", owned);
        Assert.Contains("ch-215-66.", Manager().ProcessFigure("hd-180-1.ch-215-66.lg-270-82", "M", [], 0));
    }
}
