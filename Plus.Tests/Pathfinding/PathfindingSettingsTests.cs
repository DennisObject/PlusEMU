using Plus.Core.Settings;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Rooms;
using System.Runtime.CompilerServices;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class PathfindingSettingsTests
{
    [Fact]
    public void MissingOverridesInheritProfileAndExplicitZeroIsNotMissing()
    {
        var manager = new Settings(new());
        var defaults = PathfindingSettings.Load(manager);
        Assert.Equal(PathfindingEngine.Legacy, defaults.Engine);
        Assert.Equal(1.5, defaults.EffectiveMaxUp);
        Assert.Null(defaults.EffectiveMaxDown);
        Assert.True(defaults.StacktoolLegacyCollision);
        Assert.Equal(0.05, defaults.ShadowLogSample);
        Assert.Null(defaults.MaxExpansionsPerSearch);
        manager.Values["pathfinding.profile"] = "habbo2013";
        var historical = PathfindingSettings.Load(manager);
        Assert.Equal(1.25, historical.EffectiveMaxUp);
        Assert.Equal(4, historical.EffectiveMaxDown);
        manager.Values["pathfinding.max_step_up"] = "0";
        manager.Values["pathfinding.max_step_down"] = "none";
        manager.Values["pathfinding.stacktool_legacy_collision"] = "0";
        manager.Values["pathfinding.shadow_log_sample"] = "0";
        manager.Values["pathfinding.engine"] = "shadow";
        var overrides = PathfindingSettings.Load(manager);
        Assert.Equal(0, overrides.EffectiveMaxUp);
        Assert.Null(overrides.EffectiveMaxDown);
        Assert.False(overrides.StacktoolLegacyCollision);
        Assert.Equal(0, overrides.ShadowLogSample);
        Assert.Equal(PathfindingEngine.Shadow, overrides.Engine);
        Assert.Equal(1.25, historical.EffectiveMaxUp); // Room snapshots don't follow reloads.
    }

    [Fact]
    public void InvalidNumbersCannotDisableHeightValidation()
    {
        var settings = PathfindingSettings.Load(new Settings(new()
        {
            ["pathfinding.max_step_up"] = "NaN",
            ["pathfinding.max_step_down"] = "Infinity",
            ["pathfinding.max_expansions_per_search"] = "-1",
            ["pathfinding.shadow_log_sample"] = "2"
        }));
        Assert.Equal(1.5, settings.EffectiveMaxUp);
        Assert.Null(settings.EffectiveMaxDown);
        Assert.Null(settings.MaxExpansionsPerSearch);
        Assert.Equal(1, settings.ShadowLogSample);
    }

    [Fact]
    public void GamemapCapturesNavigationSettingsWithoutOpeningLazyNavigationDependencies()
    {
        var values = new Dictionary<string, string>
        {
            ["pathfinding.engine"] = "v2",
            ["pathfinding.max_step_up"] = "2.25"
        };
        var settings = new Settings(values);
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var map = new Gamemap(room, new RoomModel("capture", 0, 0, 0, 0, "00\r00", 0, 0, false),
            TestLogging.Navigation, settings, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);

        values["pathfinding.engine"] = "legacy";
        values["pathfinding.max_step_up"] = "9";

        Assert.NotNull(map.Navigation);
        Assert.True(map.Navigation.UsesExecutor);
        Assert.Equal(2.25, map.Navigation.Settings.MaxStepUp);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("1", 1)]
    [InlineData("4", 4)]
    [InlineData("9", 4)]
    [InlineData("x", 2)]
    public void MaxSurfacesPerTileIsClampedToTheSurfaceKeyLimit(string value, int expected)
    {
        var settings = PathfindingSettings.Load(new Settings(new() { ["pathfinding.max_surfaces_per_tile"] = value }));
        Assert.Equal(expected, settings.MaxSurfacesPerTile);
        Assert.False(settings.LayeringEnabled);
    }

    private sealed class Settings(Dictionary<string, string> values) : ISettingsManager
    {
        public Dictionary<string, string> Values => values;
        public string TryGetValue(string key) => values.GetValueOrDefault(key, "0");
        public string? GetOptionalValue(string key) => values.GetValueOrDefault(key);
        public Task Reload() => Task.CompletedTask;
    }
}
