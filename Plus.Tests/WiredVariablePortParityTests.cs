using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariablePortParityTests
{
    [Theory]
    [InlineData(WiredVariableTarget.User, WiredVariableTarget.User)]
    [InlineData(WiredVariableTarget.User, WiredVariableTarget.Furni)]
    [InlineData(WiredVariableTarget.Furni, WiredVariableTarget.Furni)]
    public void ChangeUsesOneFirstReadableReferenceForEveryDestination(WiredVariableTarget target, WiredVariableTarget source)
    {
        var (module, executor, frame, destinations, operands) = Scene(target, source);
        Assert.True(executor.Execute("wf_act_change_var_val", Config(target, source, 0), frame));
        Assert.Equal(new long[] { 10, 10 }, destinations.Select(holder => module.Read(new(target, "custom:10"), holder, frame)!.Value));
        module.Mutate(new(source, "custom:11"), operands[0], WiredVariableMutation.Remove, 0, frame);
        Assert.True(executor.Execute("wf_act_change_var_val", Config(target, source, 0), frame));
        Assert.Equal(new long[] { 20, 20 }, destinations.Select(holder => module.Read(new(target, "custom:10"), holder, frame)!.Value));
    }

    [Fact]
    public void ComparisonUsesTheSameReferenceOperandForEveryTarget()
    {
        var (module, executor, frame, destinations, _) = Scene(WiredVariableTarget.User, WiredVariableTarget.User);

        foreach (var target in destinations) {
            module.Mutate(new(target.Target, "custom:10"), target, WiredVariableMutation.Set, 10, frame);
        }

        var config = Config(WiredVariableTarget.User, WiredVariableTarget.User, 2);
        Assert.True(executor.Execute("wf_cnd_var_val_match", config with { IntParams = [.. config.IntParams, 0] }, frame));
    }

    [Fact]
    public void ScalarAssignmentPublishesUnchangedValues()
    {
        var (module, executor, frame, destinations, _) = Scene(WiredVariableTarget.User, WiredVariableTarget.User);
        module.DrainChanges();
        var config = Config(WiredVariableTarget.User, WiredVariableTarget.User, 0) with
        {
            IntParams = [0, 0, 0, 0, 0, 200, 200, 201, 201]
        };
        Assert.True(executor.Execute("wf_act_change_var_val", config, frame));
        var changes = module.DrainChanges();
        Assert.Equal(destinations.Length, changes.Count);
        Assert.All(changes, change =>
        {
            Assert.Equal(WiredVariableChangeKind.Updated, change.Kind);
            Assert.Equal(change.Before!.Value, change.After!.Value);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellingArithmeticPublishesOneUnchangedBatchOrSeparateImmediateWrites(bool batched)
    {
        var (module, executor, frame, destinations, _) = Scene(WiredVariableTarget.User, WiredVariableTarget.User);
        module.DrainChanges();
        frame.VariableChanges = batched ? new() : null;
        var config = Config(WiredVariableTarget.User, WiredVariableTarget.User, 1) with
        {
            IntParams = [0, 1, 0, 3, 0, 200, 200, 201, 201]
        };
        Assert.True(executor.Execute("wf_act_change_var_val", config, frame));
        Assert.True(executor.Execute("wf_act_change_var_val", config with { IntParams = [0, 2, 0, 3, 0, 200, 200, 201, 201] }, frame));

        if (batched) {
            Assert.True(frame.VariableChanges!.Flush());
        }

        Assert.Equal(new long[] { 0, 0 }, destinations.Select(holder => module.Read(new(holder.Target, "custom:10"), holder, frame)!.Value));
        var changes = module.DrainChanges();
        Assert.Equal(destinations.Length * (batched ? 1 : 2), changes.Count);

        if (batched) {
            Assert.All(changes, change => Assert.Equal(change.Before!.Value, change.After!.Value));
        }
    }

    private static WiredConfiguration Config(WiredVariableTarget target, WiredVariableTarget source, int operation) => new()
    {
        IntParams = [(int)target, operation, 1, 0, (int)source, 200, 200, 201, 201],
        Text = "custom:10\tcustom:11\t"
    };

    private static (WiredVariableModule Module, WiredVariableExecutors Executor, WiredVariableFrame Frame,
        WiredVariableHolder[] Destinations, WiredVariableHolder[] Operands) Scene(WiredVariableTarget target, WiredVariableTarget source)
    {
        var destinations = new[] { new WiredVariableHolder(target, 1, 1), new WiredVariableHolder(target, 2, 2) };
        var operands = new[] { new WiredVariableHolder(source, 3, 3), new WiredVariableHolder(source, 4, 4) };
        var frame = new WiredVariableFrame(1, [.. destinations, .. operands]) { Signal = operands };
        frame.Selector.AddRange(destinations);
        var clock = new FixedTimeProvider(DateTimeOffset.UnixEpoch);
        var module = new WiredVariableModule(1, new Directory(target, source), new MemoryWiredVariableStore(), clock);

        foreach (var holder in destinations) {
            module.Mutate(new(target, "custom:10"), holder, WiredVariableMutation.Give, 0, frame);
        }

        for (var i = 0; i < operands.Length; i++) {
            module.Mutate(new(source, "custom:11"), operands[i], WiredVariableMutation.Give, (i + 1) * 10, frame);
        }

        return (module, new(module, clock), frame, destinations, operands);
    }

    private sealed class Directory(WiredVariableTarget target, WiredVariableTarget source) : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id is 10 or 11 ? new(id, 1, 5, $"v{id}", id == 11 ? source : target, WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
    }
}
