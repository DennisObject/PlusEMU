using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableChangedTriggerTests
{
    [Fact]
    public void ActualScalarChangesMatchTypedTriggerKindsTargetIdentityAndCreatorOrigin()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1;
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)));
        var holder = new WiredVariableHolder(WiredVariableTarget.User, 7, 1);
        var frame = new WiredVariableFrame(1, [holder]);
        var reference = new WiredVariableReference(WiredVariableTarget.User, "custom:10");
        Assert.True(WiredBoxRegistry.TryGet("wf_trg_var_changed", out var descriptor));
        var trigger = new WiredVariableChangedTrigger(room, new Item { Id = 20 }, descriptor);
        Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [0, 1, 1, 1, 0, 0, 1, 4], Text = "custom:10" }, out var config, out _));
        trigger.ApplyConfiguration(config);
        bool Matches(WiredVariableChange change) => trigger.Execute(new WiredRuntimeContext(room, new(WiredEventKind.Variable) { VariableChange = change }, new(() => [], () => []), new Operations()));
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Give, 10, frame, origin: 2));
        var created = Assert.Single(module.DrainChanges());
        Assert.True(Matches(created));
        Assert.False(Matches(created with { Origin = 0 }));
        Assert.False(Matches(created with { Key = created.Key with { Target = WiredVariableTarget.Furni } }));
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Set, 20, frame, origin: 2));
        Assert.True(Matches(Assert.Single(module.DrainChanges())));
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Set, 5, frame, origin: 2));
        Assert.False(Matches(Assert.Single(module.DrainChanges())));
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Remove, 0, frame, origin: 2));
        Assert.True(Matches(Assert.Single(module.DrainChanges())));
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, 99, frame));
        Assert.Empty(module.DrainChanges());
        Assert.False(trigger.Execute(new WiredRuntimeContext(room, new(WiredEventKind.Variable) { Code = 10, Value = 42 }, new(() => [], () => []), new Operations())));
    }
    [Fact]
    public void SuccessfulBuiltinWritesEmitActualDeltaAndFailedOrEqualWritesEmitNothing()
    {
        var builtin = new Builtin();
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)), builtin);
        var holder = new WiredVariableHolder(WiredVariableTarget.User, 7, 1);
        var frame = new WiredVariableFrame(1, [holder]);
        var reference = new WiredVariableReference(WiredVariableTarget.User, "internal:@position_x");
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Set, 5, frame, origin: 2));
        var change = Assert.Single(module.DrainChanges());
        Assert.Equal("@position.x", change.InternalKey);
        Assert.Equal(0, change.Before!.Value);
        Assert.Equal(5, change.After!.Value);
        Assert.Equal(2, change.Origin);
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, 5, frame));
        builtin.Accept = false;
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, 9, frame));
        Assert.Empty(module.DrainChanges());
        var config = new WiredConfiguration { IntParams = [0, 0, 1, 1, 1, 1, 0, -1], Text = "internal:@position.x" };
        Assert.True(WiredVariableChangedTrigger.Matches(config, change));
    }
    private sealed class Builtin : IWiredBuiltinVariables
    {
        public bool Accept = true; private int _value;
        public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame) => new(_value, null, null);
        public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame)
        {
            if (!Accept) {
                return false;
            }

            _value = value;

            return true;
        }
    }
    private sealed class Directory : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint id) => id == 1 ? 5u : null;
        public WiredVariableDefinition? Find(uint id) => id == 10 ? new(10, 1, 5, "value", WiredVariableTarget.User, WiredVariableAvailability.Persistent, true) : null;
    }
    private sealed class Operations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
