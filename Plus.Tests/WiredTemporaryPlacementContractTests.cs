using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public class WiredTemporaryPlacementContractTests
{
    [Fact]
    public void TypedPlacementRoundTripsIndependentlyOfCurrentSixEditorFields()
    {
        var old = JsonSerializer.Deserialize<WiredConfiguration>("{\"IntParams\":[21,2,3,400,4,0]}")!;
        Assert.Null(old.TemporaryPlacement);
        var typed = new WiredTemporaryPlacement(true, WiredPlaceLocationType.CustomLocation,
            WiredPlaceAltitudeType.CustomAltitude, -64, 64, -8000, true, true, int.MaxValue, 3);
        var config = old with
        {
            TemporaryPlacement = typed
        };
        var saved = JsonSerializer.Deserialize<WiredConfiguration>(JsonSerializer.Serialize(config))!;
        Assert.Equal(typed, saved.TemporaryPlacement);
        Assert.Equal(new[] { 21, 2, 3, 400, 4, 0 }, saved.IntParams);
        Assert.True(WiredLegacyProtocol.IsWithinLimits(saved));
    }

    [Fact]
    public void MalformedPlacementEnumsOffsetsAndForeignVariableTargetValuesReject()
    {
        var defaults = new WiredTemporaryPlacement();
        Assert.True(defaults.IsWithinLimits());

        foreach (var malformed in new[]
        {
            defaults with { Location = (WiredPlaceLocationType)2 }, defaults with { Altitude = (WiredPlaceAltitudeType)3 },
            defaults with { OffsetX = -65 }, defaults with { OffsetY = 65 }, defaults with { OffsetAltitudeHundredths = 8001 },
            defaults with { ValueTarget = -10 }, defaults with { ValueTarget = -20 }, defaults with { ValueTarget = 4 }
        })
        {
            Assert.False(WiredLegacyProtocol.IsWithinLimits(new()
            {
                TemporaryPlacement = malformed
            }));
        }
    }
}
