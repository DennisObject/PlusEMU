using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Boxes;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Xunit;

namespace Plus.Tests;

public sealed class WiredLegacyAddonConfigurationTests
{
    [Fact]
    public void LegacyRandomProjectsOnePickNoHistoryAndNoFurniWithoutMutatingIgnoredFields()
    {
        var item = new Item { Id = 1, Definition = new() { ItemName = "wf_xtra_random" } };
        var original = new AddonRandomEffectBox(null!, item)
        {
            StringData = "37;99",
            BoolData = true,
            ItemsData = "2:historical-unused-data"
        };
        var selected = new Item { Id = 2 };
        original.SetItems[selected.Id] = selected;
        var descriptor = item.Definition.WiredDescriptor!;

        Assert.True(WiredLegacyAddonConfigurationAdapter.TryConvert(original, descriptor, out var configuration));
        Assert.Equal(new[] { 1, 0 }, configuration.IntParams);
        Assert.Empty(configuration.Text);
        Assert.Empty(configuration.SelectedItems);
        Assert.Equal(0, configuration.SelectionCode);
        Assert.Equal(0, configuration.Delay);
        Assert.True(WiredLegacyProtocol.IsWithinLimits(configuration));
        Assert.Equal("37;99", original.StringData);
        Assert.True(original.BoolData);
        Assert.Equal("2:historical-unused-data", original.ItemsData);
        Assert.Same(selected, original.SetItems[selected.Id]);
        Assert.Same(item, original.Item);
        Assert.False(WiredLegacyAddonConfigurationAdapter.TryConvert(original,
            descriptor with
            {
                CanonicalName = "wf_xtra_unseen"
            }, out _));
        Assert.False(WiredLegacyAddonConfigurationAdapter.TryConvert(original,
            descriptor with
            {
                Category = WiredBoxCategory.Action
            }, out _));
    }
}
