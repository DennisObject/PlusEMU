using Microsoft.Extensions.Logging.Abstractions;
using Plus.Core.FigureData;
using Xunit;

namespace Plus.Tests;

public class FigureDataManagerTests
{
    private static FigureDataManager Manager()
    {
        // Init reads Config/figuredata.xml from the working directory (the test output folder).
        // The catalog is only consulted when clothing parts are passed.
        var manager = new FigureDataManager(null!, NullLogger<FigureDataManager>.Instance);
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
        var processed = Manager().ProcessFigure(figure, "M", null!, true);

        Assert.Contains("hd-180-", processed);
        Assert.All(processed.TrimEnd('.').Split('.'), part => Assert.Matches(@"^[a-z]{2}-\d+-\d+(-\d+)?$", part));
    }

    [Fact]
    public void WellFormedFiguresKeepTheirParts()
    {
        var processed = Manager().ProcessFigure("hd-180-1.ch-215-66.lg-270-82.sh-290-80", "M", null!, true);

        Assert.Contains("hd-180-1.", processed);
        Assert.Contains("ch-215-66.", processed);
    }
}
