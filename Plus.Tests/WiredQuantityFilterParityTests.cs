using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Xunit;
using static Plus.Tests.WiredSelectorTests;

namespace Plus.Tests;

public sealed class WiredQuantityFilterParityTests
{
    [Fact]
    public void QuantityFilterUsesExplicitSelectorOrderRatherThanSetEnumeration()
    {
        var selected = new WiredSelectedIds();
        selected.FurniIds.UnionWith(new uint[] { 1, 2, 3 });
        selected.UserIds.UnionWith(new[] { 1, 2, 3 });
        var policy = new WiredAddonPolicy { FurniLimit = 1, UserLimit = 1 };
        var filtered = policy.FilterSelection(selected, new uint[] { 3, 1, 2 }, new[] { 2, 3, 1 });
        Assert.Equal(new uint[] { 3 }, filtered.FurniIds);
        Assert.Equal(new[] { 2 }, filtered.UserIds);
        Assert.Equal(3, selected.FurniIds.Count);
        Assert.Equal(3, selected.UserIds.Count);
    }

    [Theory]
    [InlineData("wf_xtra_filter_furni", 2, 2)]
    [InlineData("wf_xtra_filter_users", 2, 2)]
    [InlineData("wf_xtra_filter_furni", -3, 0)]
    [InlineData("wf_xtra_filter_users", 0, 0)]
    public void VariableCountKeepsFirstSelectorEntriesAndUsesSelectedHolderOperand(string name, long value, int expected)
    {
        var configuration = new WiredConfiguration { IntParams = [3, 1, 0], VariableIds = ["variable:7"] };
        var addon = new WiredAddonModule(name, configuration);
        var requests = new List<WiredAddonVariableRequest>();
        var input = new WiredAddonInputs(World(), Inputs(), 0, request => { requests.Add(request); return value; });
        var policy = new WiredAddonPolicy();
        Assert.True(addon.Apply(input, policy));
        var request = Assert.Single(requests);
        Assert.Equal("variable:7", request.Token);
        Assert.Equal(0, request.Target);
        Assert.True(request.UseSelected);
        var pool = new WiredSelectedIds();
        pool.FurniIds.UnionWith(new uint[] { 3, 2, 1 });
        pool.UserIds.UnionWith(new[] { 3, 2, 1 });
        var result = policy.FilterSelection(pool);
        Assert.Equal(expected, name.EndsWith("furni") ? result.FurniIds.Count : result.UserIds.Count);
        Assert.Equal(expected == 2 ? new[] { 3, 2 } : [], name.EndsWith("furni") ? result.FurniIds.Select(id => (int)id) : result.UserIds);
        Assert.Equal(3, name.EndsWith("furni") ? result.UserIds.Count : result.FurniIds.Count);
    }

    [Theory]
    [InlineData("wf_xtra_filter_furni")]
    [InlineData("wf_xtra_filter_users")]
    public void MissingVariableFallsBackToLiteralAndLegacyZeroRemainsUnlimited(string name)
    {
        var input = new WiredAddonInputs(World(), Inputs(), 0, _ => null);
        var policy = new WiredAddonPolicy();
        var addon = new WiredAddonModule(name, new() { IntParams = [2, 1, 3], VariableIds = ["missing"] });
        Assert.True(addon.Apply(input, policy));
        Assert.Equal(2, name.EndsWith("furni") ? policy.FurniLimit : policy.UserLimit);
        var legacy = new WiredAddonModule(name, new() { IntParams = [0] });
        var unlimited = new WiredAddonPolicy();
        Assert.True(legacy.Apply(input, unlimited));
        Assert.Null(name.EndsWith("furni") ? unlimited.FurniLimit : unlimited.UserLimit);
        Assert.Single(legacy.Configuration.IntParams);
    }
}
