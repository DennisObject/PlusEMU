using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredContextScopePortTests
{
    [Fact]
    public void ChildrenShareInheritedChangesButOverrideLocallyAndRevealTheParentAfterRemoval()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1;
        var parent = WiredVariableRuntimeFrames.Create(Context(room));
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var reference = new WiredVariableReference(WiredVariableTarget.Context, "custom:10");
        var holder = new WiredVariableHolder(WiredVariableTarget.Context, 0, 0);
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Give, 7, parent));
        var child = WiredVariableRuntimeFrames.Fork(Context(room), parent);
        var sibling = WiredVariableRuntimeFrames.Fork(Context(room), parent);
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Give, 99, child));
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Replace, 9, child));
        Assert.Equal((7, 9, 7), (Read(parent), Read(child), Read(sibling)));
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Set, 10, child));
        Assert.Equal((7, 10, 7), (Read(parent), Read(child), Read(sibling)));
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Remove, 0, child));
        Assert.Equal((7, 7, 7), (Read(parent), Read(child), Read(sibling)));
        Assert.True(module.Change(reference, holder, WiredVariableMutation.Set, value => value + 4, child));
        Assert.Equal((11, 11, 11), (Read(parent), Read(child), Read(sibling)));

        using (var reads = module.CaptureReads([reference], sibling)) {
            Assert.Equal(11, reads.Read(reference, holder, sibling)!.Value);
        }

        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Remove, 0, sibling));
        Assert.Null(Read(parent));
        Assert.Null(Read(child));
        Assert.Null(Read(sibling));

        long? Read(WiredVariableFrame frame) => module.Read(reference, holder, frame)?.Value;
    }

    [Fact]
    public void RefreshRetainsItsOwnShadowAndNewVariablesStayInTheirChildScope()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1;
        var parent = WiredVariableRuntimeFrames.Create(Context(room));
        var runtime = Context(room);
        var child = WiredVariableRuntimeFrames.Fork(runtime, parent);
        var sibling = WiredVariableRuntimeFrames.Fork(Context(room), parent);
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var reference = new WiredVariableReference(WiredVariableTarget.Context, "custom:10");
        var holder = new WiredVariableHolder(WiredVariableTarget.Context, 0, 0);
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Give, 7, child));
        Assert.Null(module.Read(reference, holder, parent));
        Assert.Null(module.Read(reference, holder, sibling));
        var refreshed = WiredVariableRuntimeFrames.Create(runtime, child);
        Assert.Same(child.Context, refreshed.Context);
        Assert.Equal(7, module.Read(reference, holder, refreshed)!.Value);
    }

    private static WiredRuntimeContext Context(Room room) => new(room, new(WiredEventKind.Signal), new(() => Array.Empty<Item>(), () => Array.Empty<RoomUser>()), new Operations());
    private sealed class Directory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id == 10 ? new(id, 1, 5, "scope", WiredVariableTarget.Context, WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
    }
    private sealed class Operations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
