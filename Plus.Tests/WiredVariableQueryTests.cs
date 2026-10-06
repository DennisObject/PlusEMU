using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableQueryTests
{
    [Fact]
    public void SelectorCapturesReferencesOnceAndPrefersMatchingHolderThenFirstReference()
    {
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var first = new WiredVariableHolder(WiredVariableTarget.User, 901, 1);
        var second = new WiredVariableHolder(WiredVariableTarget.User, 902, 2);
        var third = new WiredVariableHolder(WiredVariableTarget.User, 903, 3);
        var frame = new WiredVariableFrame(1, [first, second, third]);
        frame.Selector.AddRange([first, second]);
        var main = new WiredVariableReference(first.Target, "custom:10");
        var reference = new WiredVariableReference(first.Target, "custom:11");

        foreach (var holder in frame.Holders) {
            module.Mutate(main, holder, WiredVariableMutation.Give, holder == second ? 20 : 10, frame);
        }

        module.Mutate(reference, first, WiredVariableMutation.Give, 10, frame);
        module.Mutate(reference, second, WiredVariableMutation.Give, 20, frame);
        var config = new WiredConfiguration { IntParams = [1, 2, 1, 0, 0, 200, 0, 0, 0], Text = "custom:10\tcustom:11" };

        using (var queries = new WiredVariableQueries(module, frame)) {
            Assert.True(queries.MatchSelector("wf_slc_users_with_var", config, first));
            module.Mutate(reference, second, WiredVariableMutation.Set, 99, frame);
            Assert.True(queries.MatchSelector("wf_slc_users_with_var", config, second));
            Assert.True(queries.MatchSelector("wf_slc_users_with_var", config, third));
        }

        using var fresh = new WiredVariableQueries(module, frame);
        Assert.False(fresh.MatchSelector("wf_slc_users_with_var", config, second));
    }

    [Fact]
    public void SelectedFurnitureOperandUsesSerializedSelectionAndAbsentIsNull()
    {
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var holder = new WiredVariableHolder(WiredVariableTarget.Furni, 300, 300);
        var frame = new WiredVariableFrame(1, [holder]);
        module.Mutate(new(holder.Target, "custom:12"), holder, WiredVariableMutation.Give, 42, frame);
        using var queries = new WiredVariableQueries(module, frame);
        Assert.Equal(42L, queries.ReadOperand(holder.Target, "custom:12", 0, 101, new() { SelectedItems = [300] }));
        Assert.Null(queries.ReadOperand(holder.Target, "custom:12", 0, 101, new() { SelectedItems = [301] }));
        Assert.Null(queries.ReadOperand(holder.Target, "custom:99", 0, 101, new() { SelectedItems = [300] }));
    }

    private sealed class Directory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id is >= 10 and <= 12
            ? new(id, 1, 5, $"v{id}", id == 12 ? WiredVariableTarget.Furni : WiredVariableTarget.User, WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
    }
}
