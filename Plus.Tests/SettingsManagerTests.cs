using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public sealed class SettingsManagerTests
{
    [Fact]
    public async Task ReloadKeepsMixedCaseValuesVerbatim()
    {
        const string url = "https://Cdn.Example.COM/Gamedata/Furnidata.xml?Token=AbC123";
        var settings = await Load(("hotel.asset.url", url));

        Assert.Equal(url, settings.TryGetValue("hotel.asset.url"));
        Assert.Equal(url, settings.GetOptionalValue("hotel.asset.url"));
    }

    [Theory]
    [InlineData("v2", "habbo2013", "none", "strict", "nearest")]
    [InlineData("V2", "Habbo2013", "None", "Strict", "Nearest")]
    [InlineData("V2", "HABBO2013", "NONE", "STRICT", "NEAREST")]
    public async Task PathfindingKeywordsStayCaseInsensitive(string engine, string profile, string down, string corner, string unreachable)
    {
        var settings = PathfindingSettings.Load(await Load(("pathfinding.engine", engine), ("pathfinding.profile", profile),
            ("pathfinding.max_step_down", down), ("pathfinding.corner_rule", corner), ("pathfinding.unreachable_policy", unreachable),
            ("pathfinding.layering_enabled", "1"), ("pathfinding.stacktool_legacy_collision", "0")));

        Assert.Equal(PathfindingEngine.V2, settings.Engine);
        Assert.Equal("habbo2013", settings.Profile);
        Assert.True(settings.UnlimitedDown);
        Assert.Equal(CornerRule.Strict, settings.CornerRule);
        Assert.Equal("nearest", settings.UnreachablePolicy);
        Assert.True(settings.LayeringEnabled);
        Assert.False(settings.StacktoolLegacyCollision);
    }

    private static async Task<SettingsManager> Load(params (string Key, string Value)[] rows)
    {
        var table = new DataTable();
        table.Columns.Add("key", typeof(string));
        table.Columns.Add("value", typeof(string));

        foreach (var (key, value) in rows) {
            table.Rows.Add(key, value);
        }

        var settings = new SettingsManager(new Database(table), NullLogger<SettingsManager>.Instance);
        await settings.Reload();

        return settings;
    }

    private sealed class Database(DataTable table) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new RoomModelAccessTests.ReaderConnection(table, null);
    }
}
