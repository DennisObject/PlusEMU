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
        var box = Box(name); var initial = box.Configuration;
        Assert.True(box.TryValidateConfiguration(initial, out _, out _)); Assert.False(box.Execute());
        Assert.False(box.TryValidateConfiguration(initial with { IntParams = initial.IntParams.SetItem(1, 3), Text = "" }, out _, out _));
        Assert.Equal(initial, box.Configuration);
    }
    [Fact]
    public void LevelAndTextMetadataPublishOnlyValidatedImmutableSettings()
    {
        var box = Box("wf_xtra_var_text_connector");
        Assert.True(box.TryValidateConfiguration(new() { Text = "1=one\n2=two" }, out var validated, out _));
        Assert.Empty(box.TextConnector); box.ApplyConfiguration(validated);
        Assert.Equal("two", box.TextConnector[2]);
        Assert.False(box.TryValidateConfiguration(new() { Text = "not-an-integer=value" }, out _, out _));
        Assert.Equal("two", box.TextConnector[2]);
        var level = Box("wf_xtra_var_lvlup_system"); Assert.NotNull(level.LevelSystem);
        Assert.Equal(2, level.LevelSystem.Level(150).Level); Assert.False(level.Execute());
    }
    private static WiredVariableMetadataBox Box(string name)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        return new((Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)), new Item { Id = 10 }, descriptor);
    }
}
