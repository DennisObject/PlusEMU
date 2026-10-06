using System.Text.Json;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.HabboHotel.Items;
using Xunit;

namespace Plus.Tests;

public class MagicTileHeightTests
{
    [Fact]
    public void OnlyMagicTilesCanBeAdjusted()
    {
        Assert.True(MagicTileHeight.IsMagicTile(InteractionType.Stacktool));
        Assert.True(MagicTileHeight.IsMagicTile(InteractionType.WalkMagicTile));
        Assert.False(MagicTileHeight.IsMagicTile(InteractionType.None));
        Assert.False(MagicTileHeight.IsMagicTile(InteractionType.Gate));
        Assert.False(MagicTileHeight.IsMagicTile(InteractionType.Teleport));
    }

    [Theory]
    [InlineData(150, 0.0, 0.0, 1.5)]
    [InlineData(0, 0.0, 3.0, 0.0)]
    [InlineData(400000, 0.0, 0.0, 40.0)]
    [InlineData(int.MaxValue, 0.0, 0.0, 40.0)]
    [InlineData(-5000, 0.0, 0.0, 0.0)]
    [InlineData(-1, 2.0, 2.0, 2.0)]
    [InlineData(100, 2.0, 2.0, 2.0)]
    public void ManualHeightsAreClampedBetweenFloorAndLimit(int requested, double floor, double stackBelow, double expected) =>
        Assert.Equal(expected, MagicTileHeight.Resolve(requested, floor, stackBelow));

    [Theory]
    [InlineData(0.0, 2.35, 2.35)]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(0.0, 87.5, 40.0)]
    public void MatchBelowUsesTheStackAndIsStillClamped(double floor, double stackBelow, double expected) =>
        Assert.Equal(expected, MagicTileHeight.Resolve(MagicTileHeight.MatchBelow, floor, stackBelow));

    [Fact]
    public void WireHeightIsHundredths() => Assert.Equal(235, MagicTileHeight.ToWire(2.35));

    [Fact]
    public void OctaneRevisionSendsTheHeightEchoOnTheStackHelperHeader()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RevisionPath("OCTANE-3-6-0-FLOOR-20260909.json")));
        var outgoing = document.RootElement.GetProperty("OutgoingHeaders");
        Assert.Equal(2816, outgoing.GetProperty("UpdateMagicTileComposer").GetInt32());
        Assert.Equal(3839, document.RootElement.GetProperty("IncomingHeaders").GetProperty("UpdateMagicTileEvent").GetInt32());
    }

    private static string RevisionPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Resources", "Revisions", fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(fileName);
    }
}
