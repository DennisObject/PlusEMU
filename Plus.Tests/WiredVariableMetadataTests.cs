using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableMetadataTests
{
    [Theory]
    [InlineData("wf_xtra_var_fx_health")]
    [InlineData("wf_xtra_var_fx_progress")]
    [InlineData("wf_xtra_var_fx_level")]
    [InlineData("wf_xtra_var_fx_status")]
    [InlineData("wf_xtra_var_fx_boss")]
    [InlineData("wf_xtra_var_fx_number")]
    public void FxMetadataHasRealClientDefaultsAndRejectsInvalidAudienceWithoutPublication(string name)
    {
        var box = Box(name);
        var initial = box.Configuration;
        Assert.True(box.TryValidateConfiguration(initial, out _, out _));
        Assert.False(box.Execute());
        // An audience-restricted display without an audience variable has no valid runtime at all.
        var native = WiredNativeEditorProjection.DefaultNative(box.Descriptor);
        Assert.False(WiredNativeEditorProjection.TryCompile(10, box.Descriptor, native with { OwnedIntParams = native.OwnedIntParams.SetItem(1, 3) }, out _));
        Assert.Equal(initial, box.Configuration);
    }
    [Fact]
    public void LevelAndTextMetadataPublishOnlyValidatedImmutableSettings()
    {
        var box = Box("wf_xtra_var_text_connector");
        var connector = WiredNativeTestSupport.FromLegacyVariableDraft(box.Descriptor, new() { Text = "1=one\n2=two" });
        Assert.True(WiredNativeEditorProjection.TryCompile(10, box.Descriptor, connector, out var runtime));
        Assert.True(box.TryValidateConfiguration(runtime, out var validated, out _));
        Assert.Empty(box.TextConnector);
        box.ApplyConfiguration(validated);
        Assert.Equal("two", box.TextConnector[2]);
        Assert.False(WiredNativeEditorProjection.TryCompile(10, box.Descriptor, connector with { Text = "not-an-integer=value" }, out _));
        Assert.Equal("two", box.TextConnector[2]);
        var level = Box("wf_xtra_var_lvlup_system");
        Assert.NotNull(level.LevelSystem);
        Assert.Equal(2, level.LevelSystem.Level(150).Level);
        Assert.False(level.Execute());
        WiredNativeTestSupport.InstallLegacyVariableDraft(level, new() { Text = "{\"mode\":2,\"firstLevelXp\":100,\"increaseFactor\":0,\"maxLevel\":3}" });
        Assert.Equal(200, level.LevelSystem!.Level(150).Next);
    }
    [Fact]
    public void TimeUsesActiveEditorMask()
    {
        var time = Box("wf_xtra_var_time_util");
        // Anything outside the editor's subvariable mask or its three clocks has no valid record.
        Assert.False(WiredNativeEditorProjection.TryCompile(10, time.Descriptor, WiredNativeTestSupport.FromLegacyVariableDraft(time.Descriptor, new() { IntParams = [-1, 0] }), out _));
        Assert.False(WiredNativeEditorProjection.TryCompile(10, time.Descriptor, WiredNativeTestSupport.FromLegacyVariableDraft(time.Descriptor, new() { IntParams = [0, 9] }), out _));
        WiredNativeTestSupport.InstallLegacyVariableDraft(time, new() { IntParams = [WiredVariableTimeUtilities.ValidMask, 0] });
        Assert.Equal(WiredVariableTimeUtilities.ValidMask, time.TimeUtilities!.Mask);
        Assert.Equal(0, time.TimeUtilities.Mode);
        Assert.Equal(17, time.TimeUtilities.Selected.Count());
        Assert.False(time.TimeUtilities.Has(0));
    }

    private static WiredVariableMetadataBox Box(string name)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));

        return new((Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)), new Item { Id = 10 }, descriptor);
    }
}
