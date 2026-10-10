using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredScalarLiteralParityTests
{
    [Theory]
    [InlineData(long.MinValue, false)]
    [InlineData(long.MaxValue, false)]
    [InlineData(9007199254740993L, false)]
    [InlineData(long.MinValue, true)]
    [InlineData(long.MaxValue, true)]
    [InlineData(9007199254740993L, true)]
    public void SavedWideGiveChangeAndQuantifiedMatchExecuteExactLiterals(long literal, bool batch)
    {
        var holders = new[] { new WiredVariableHolder(WiredVariableTarget.Furni, 1, 1), new WiredVariableHolder(WiredVariableTarget.Furni, 2, 2) };
        var frame = new WiredVariableFrame(1, holders) { Trigger = holders, VariableChanges = batch ? new() : null };
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), TimeProvider.System);
        var executor = new WiredVariableExecutors(module, TimeProvider.System);
        // The persisted suffix remains on the stored config; only runtime decoding strips it.
        WiredConfiguration Reload(int[] values) => JsonSerializer.Deserialize<WiredConfiguration>(JsonSerializer.Serialize(new WiredConfiguration { IntParams = [.. values], Text = "custom:10" }))!;
        var low = unchecked((int)literal);
        var high = unchecked((int)(literal >> 32));
        Assert.True(executor.Execute("wf_act_give_var", WiredNativeTestSupport.Scalar("wf_act_give_var", Reload([1, 0, low, 0, 0, 1, high])), frame));
        Assert.Equal(new[] { literal, literal }, holders.Select(holder => module.Read(new(holder.Target, "custom:10"), holder, frame)!.Value));
        var change = Reload([1, 1, 0, low, 1, 0, 0, 0, 0, 1, high]);
        Assert.True(executor.Execute("wf_act_change_var_val", WiredNativeTestSupport.Scalar("wf_act_change_var_val", change), frame));

        if (batch) {
            Assert.True(frame.VariableChanges!.Flush());
        }

        var result = unchecked(literal + literal);
        Assert.Equal(new[] { result, result }, holders.Select(holder => module.Read(new(holder.Target, "custom:10"), holder, frame)!.Value));
        module.Mutate(new(holders[0].Target, "custom:10"), holders[0], WiredVariableMutation.Set, literal, frame);
        // High word is not the quantifier. Quantifier remains at original slot nine.
        var all = Reload([1, 2, 0, low, 1, 0, 0, 0, 0, 0, 1, high]);
        var any = all with { IntParams = all.IntParams.SetItem(9, 1) };
        Assert.Equal(result == literal, executor.Execute("wf_cnd_var_val_match", WiredNativeTestSupport.Scalar("wf_cnd_var_val_match", all), frame));
        Assert.True(executor.Execute("wf_cnd_var_val_match", WiredNativeTestSupport.Scalar("wf_cnd_var_val_match", any), frame));
    }

    [Fact]
    public void LegacyNegativeLiteralAndInactiveWideOperandKeepTheirMeanings()
    {
        var holder = new WiredVariableHolder(WiredVariableTarget.Context, 0, 0);
        var frame = new WiredVariableFrame(1, []) { Trigger = [holder] };
        var module = new WiredVariableModule(1, new Directory(WiredVariableTarget.Context), new MemoryWiredVariableStore(), TimeProvider.System);
        var executor = new WiredVariableExecutors(module, TimeProvider.System);
        Assert.True(executor.Execute("wf_act_give_var", WiredNativeTestSupport.Scalar("wf_act_give_var", new() { IntParams = [2, 0, -7, 0, 0], Text = "custom:10" }), frame));
        Assert.Equal(-7, module.Read(new(holder.Target, "custom:10"), holder, frame)!.Value);
        Assert.True(executor.Execute("wf_act_give_var", WiredNativeTestSupport.Scalar("wf_act_give_var", new() { IntParams = [2, 0, 3, 0, 0], Text = "custom:11" }), frame));
        Assert.True(executor.Execute("wf_act_change_var_val", WiredNativeTestSupport.Scalar("wf_act_change_var_val", new() { IntParams = [2, 1, 1, -1, 2, 0, 0, 0, 0, 1, int.MinValue], Text = "custom:10\tcustom:11" }), frame));
        Assert.Equal(-4, module.Read(new(holder.Target, "custom:10"), holder, frame)!.Value);
    }

    [Theory]
    [InlineData("wf_act_give_var", new[] { 1, 0, 1, 0, 0, 1 })]
    [InlineData("wf_act_give_var", new[] { 1, 0, 1, 0, 0, 2, 0 })]
    [InlineData("wf_act_give_var", new[] { 1, 0, 1, 0, 0, 1, 0, 0 })]
    [InlineData("wf_act_change_var_val", new[] { 1, 0, 0, 1, 1, 0, 0, 0, 0, 1 })]
    [InlineData("wf_cnd_var_val_match", new[] { 1, 2, 0, 1, 1, 0, 0, 0, 0, 1, 2, 0 })]
    [InlineData("wf_cnd_var_val_match", new[] { 1, 2, 0, 1, 1, 0, 0, 0, 0, 2, 1, 0 })]
    public void MalformedSuffixesAndOriginalQuantifiersAreRejected(string name, int[] fields)
    {
        Assert.False(WiredVariableExecutors.TryValidate(name, WiredNativeTestSupport.Scalar(name, new() { IntParams = [.. fields], Text = "custom:10" }), out _));
    }

    private sealed class Directory(WiredVariableTarget target = WiredVariableTarget.Furni) : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id is 10 or 11 ? new(id, 1, 5, "test" + id, target, WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => 5;
    }
}
