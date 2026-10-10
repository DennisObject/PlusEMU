using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Variables.Fx;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableFxTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedRoomUnitIdInitializesReplacementAndRejectsDetachedPendingBatch(bool samePlayer)
    {
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var viewer = new WiredVariableHolder(WiredVariableTarget.User, 901, 8);
        var old = new WiredVariableHolder(WiredVariableTarget.User, 900, 7);
        var replacement = old with { StableId = samePlayer ? old.StableId : 902 };
        var reference = new WiredVariableReference(WiredVariableTarget.User, "custom:10");
        var before = new WiredVariableFrame(1, [viewer, old]);
        module.Mutate(reference, old, WiredVariableMutation.Give, 25, before);
        var binding = new WiredVariableFxBinding(new(50, true, 0, 3000, 0, 0, 0, 0, 0, 0, 100, ImmutableSortedDictionary<string, string>.Empty), reference, 2, null, 0, null, null);
        var tracker = new WiredVariableFxTracker(module);
        var first = tracker.Update(viewer, before, [binding], [old], _ => 0);
        Assert.True(tracker.Acknowledge(viewer.StableId, first));
        var pending = tracker.Update(viewer, before, [binding], [old], _ => 0);
        var after = new WiredVariableFrame(1, [viewer, replacement]);

        if (!samePlayer) {
            module.Mutate(reference, replacement, WiredVariableMutation.Give, 25, after);
        }

        // Identity discrimination also catches a different holder before a detach callback arrives.
        if (!samePlayer) {
            Assert.True(Assert.Single(tracker.Update(viewer, after, [binding], [replacement], _ => 0).Statuses).Initialize);
        }

        tracker.DetachHolder(old);
        Assert.False(tracker.Acknowledge(viewer.StableId, pending));
        var reentered = tracker.Update(viewer, after, [binding], [replacement], _ => 0);
        Assert.True(Assert.Single(reentered.Statuses).Initialize);
        Assert.Equal(7, reentered.Statuses[0].Key.EntityId);
    }

    [Fact]
    public void OverrideUsesStablePlayerIdWhileWireUsesEntityIdAndAudienceLossRemovesStatus()
    {
        var directory = new Directory();
        var store = new MemoryWiredVariableStore();
        var module = new WiredVariableModule(1, directory, store, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var holder = new WiredVariableHolder(WiredVariableTarget.User, 900, 7);
        var viewer = new WiredVariableHolder(WiredVariableTarget.User, 901, 8);
        var frame = new WiredVariableFrame(1, [holder, viewer]);
        var score = new WiredVariableReference(WiredVariableTarget.User, "custom:10");
        var max = new WiredVariableReference(WiredVariableTarget.User, "custom:11");
        var audience = new WiredVariableReference(WiredVariableTarget.User, "custom:12");
        module.Mutate(score, holder, WiredVariableMutation.Give, 25, frame);
        module.Mutate(max, holder, WiredVariableMutation.Give, 200, frame);
        module.Mutate(audience, viewer, WiredVariableMutation.Give, 1, frame);
        Assert.True(WiredBoxRegistry.TryGet("wf_xtra_var_fx_progress", out var descriptor));
        // User source, visible to its audience, classic progress bar, range maximum read from custom:11.
        var native = WiredNativeEditorProjection.DefaultNative(descriptor) with
        {
            OwnedIntParams = [1, 3, 0, 0, 0, 3000, 0, 1, 2, 0, 0, 0, 0, 100, 0, 1, 1, 1, 0, 0, 0],
            VariableIds = ["n", "user:11", "user:12"]
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(50, descriptor, native, out var configuration));
        Assert.True(WiredVariableFxSettings.TryDecode("wf_xtra_var_fx_progress", 50, configuration, score, null, out var binding, out _));
        var tracker = new WiredVariableFxTracker(module);
        var first = tracker.Update(viewer, frame, [binding!], [holder], _ => 0);
        Assert.True(first.InitializeAll);
        Assert.Single(first.Configs);
        var status = Assert.Single(first.Statuses);
        Assert.Equal(7, status.Key.EntityId);
        Assert.Equal("user:10", status.Key.VariableId);
        Assert.Equal(200, status.Max);
        Assert.Equal(25, status.Value);
        Assert.Equal(new uint[] { 9473, 9475 }, WiredVariableFxComposer.ComposeBatch(first).Select(x => x.MessageId));
        var retry = tracker.Update(viewer, frame, [binding!], [holder], _ => 0);
        Assert.Single(retry.Configs);
        Assert.Single(retry.Statuses); // Failed send was not acknowledged.
        Assert.False(tracker.Acknowledge(viewer.StableId, first));
        Assert.True(tracker.Acknowledge(viewer.StableId, retry));
        var unchanged = tracker.Update(viewer, frame, [binding!], [holder], _ => 0);
        Assert.Empty(unchanged.Statuses);
        Assert.Empty(unchanged.Configs);
        module.Mutate(audience, viewer, WiredVariableMutation.Remove, 0, frame);
        var lost = tracker.Update(viewer, frame, [binding!], [holder], _ => 0);
        Assert.Equal(status.Key, Assert.Single(lost.RemovedStatuses));
        Assert.Empty(lost.Statuses);
    }

    [Fact]
    public void WireSignedLongAndRemovalKeyMatchClientReader()
    {
        var key = new WiredVariableFxKey(40, "custom:22", true, 7);
        var status = new WiredVariableFxStatus(key, true, -4294967297L, null, null, ImmutableSortedDictionary<string, string>.Empty);
        var batch = new WiredVariableFxBatch(true, [], [], [status], []);
        var packet = new Packet();
        WiredVariableFxComposer.ComposeBatch(batch).Single().Compose(packet);
        Assert.Equal(new object[] { true, 1, "40|custom:22", true, true, 7, -2, -1, false, 0 }, packet.Values);
        var removed = new Packet();
        WiredVariableFxComposer.ComposeBatch(batch with { Statuses = [], RemovedStatuses = [key] }).Single().Compose(removed);
        Assert.Equal(new object[] { 1, "40|custom:22|u|7" }, removed.Values);
    }

    [Fact]
    public void ScalarEditorExecutionChangesSelectedStableHolderAndChecksQuantifier()
    {
        var directory = new Directory();
        var module = new WiredVariableModule(1, directory, new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var holder = new WiredVariableHolder(WiredVariableTarget.User, 321, 8);
        var other = new WiredVariableHolder(WiredVariableTarget.User, 123, 9);
        var frame = new WiredVariableFrame(1, [holder, other]) { Trigger = [holder] };
        var executor = new WiredVariableExecutors(module, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(2000)));
        Assert.True(executor.Execute("wf_act_give_var", WiredNativeTestSupport.Scalar("wf_act_give_var", new() { IntParams = [0, 0, 10, 0, 0], Text = "10" }), frame));
        Assert.True(executor.Execute("wf_act_change_var_val", WiredNativeTestSupport.Scalar("wf_act_change_var_val", new() { IntParams = [0, 1, 0, 5, 0, 0, 0, 0, 0], Text = "custom:10\t\t" }), frame));
        Assert.Equal(15, module.Read(new(WiredVariableTarget.User, "custom:10"), holder, frame)!.Value);
        Assert.True(executor.Execute("wf_cnd_var_val_match", WiredNativeTestSupport.Scalar("wf_cnd_var_val_match", new() { IntParams = [0, 2, 0, 15, 0, 0, 0, 0, 0, 0], Text = "custom:10\t\t" }), frame));
        frame.Selector.AddRange([holder, other]);
        Assert.False(executor.Execute("wf_cnd_has_var", WiredNativeTestSupport.Scalar("wf_cnd_has_var", new() { IntParams = [0, 200, 0, 0], Text = "custom:10" }), frame));
        Assert.True(executor.Execute("wf_cnd_has_var", WiredNativeTestSupport.Scalar("wf_cnd_has_var", new() { IntParams = [0, 200, 0, 1], Text = "custom:10" }), frame));
    }

    private sealed class Directory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id is >= 10 and <= 12
            ? new(id, 1, 5, $"v{id}", WiredVariableTarget.User, WiredVariableAvailability.Persistent, true) : null;
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
    }
    private sealed class Packet : IOutgoingPacket
    {
        public List<object> Values { get; } = [];
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => default;
        public void WriteByte(byte value) => Values.Add(value);
        public void WriteShort(short value) => Values.Add(value);
        public void WriteInt(int value) => Values.Add(value);
        public void WriteInteger(int value) => Values.Add(value);
        public void WriteUInt(uint value) => Values.Add(value);
        public void WriteUInteger(uint value) => Values.Add(value);
        public void WriteBool(bool value) => Values.Add(value);
        public void WriteBoolean(bool value) => Values.Add(value);
        public void WriteString(string value) => Values.Add(value);
        public void WriteDouble(double value) => Values.Add(value);
    }
}
