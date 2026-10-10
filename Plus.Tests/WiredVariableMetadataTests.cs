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
        Assert.False(box.TryValidateConfiguration(initial with { IntParams = initial.IntParams.SetItem(1, 3), Text = "" }, out _, out _));
        Assert.Equal(initial, box.Configuration);
    }
    [Fact]
    public void LevelAndTextMetadataPublishOnlyValidatedImmutableSettings()
    {
        var box = Box("wf_xtra_var_text_connector");
        Assert.True(box.TryValidateConfiguration(new() { Text = "1=one\n2=two" }, out var validated, out _));
        Assert.Empty(box.TextConnector);
        box.ApplyConfiguration(validated);
        Assert.Equal("two", box.TextConnector[2]);
        Assert.False(box.TryValidateConfiguration(new() { Text = "not-an-integer=value" }, out _, out _));
        Assert.Equal("two", box.TextConnector[2]);
        var level = Box("wf_xtra_var_lvlup_system");
        Assert.NotNull(level.LevelSystem);
        Assert.Equal(2, level.LevelSystem.Level(150).Level);
        Assert.False(level.Execute());
        Assert.True(level.TryValidateConfiguration(new() { Text = "{\"mode\":2,\"firstLevelXp\":100,\"increaseFactor\":0,\"maxLevel\":3}" }, out var zeroGrowth, out _));
        level.ApplyConfiguration(zeroGrowth);
        Assert.Equal(200, level.LevelSystem!.Level(150).Next);
    }
    [Fact]
    public void QuestAndChainReadActualCountersAndTimeUsesActiveEditorMask()
    {
        var quest = Box("wf_var_quest");
        Assert.True(quest.TryValidateConfiguration(new() { IntParams = [50] }, out var candidate, out _));
        quest.ApplyConfiguration(candidate);
        Assert.Equal(new long[] { 25, 50, 0, 50, 25 }, Enumerable.Range(0, 5).Select(sub => quest.ReadDerived(25, sub)));
        Assert.Equal(1, quest.ReadDerived(50, 2));
        Assert.False(quest.Execute());
        var chain = Box("wf_var_quest_chain");
        Assert.True(chain.TryValidateConfiguration(new() { IntParams = [3] }, out candidate, out _));
        chain.ApplyConfiguration(candidate);
        Assert.Equal(new long[] { 3, 3, 1, 100 }, Enumerable.Range(0, 4).Select(sub => chain.ReadDerived(4, sub)));
        Assert.True(chain.TryValidateConfiguration(new() { IntParams = [-1] }, out candidate, out _));
        chain.ApplyConfiguration(candidate);
        Assert.Equal(0, chain.QuestTarget);
        Assert.Equal(0, chain.ReadDerived(4, 2));
        Assert.Equal(100, chain.ReadDerived(4, 3));
        var time = Box("wf_xtra_var_time_util");
        Assert.True(time.TryValidateConfiguration(new() { IntParams = [-1, 9] }, out candidate, out _));
        time.ApplyConfiguration(candidate);
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
