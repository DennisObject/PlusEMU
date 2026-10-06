using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Variables.Fx;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableReadSnapshotTests
{
    [Fact]
    public void TwoHundredUsersAndSixFxBatchDefinitionAndValueReadsAcrossViewers()
    {
        var directory = new Directory();
        var store = new CountingStore();
        var module = new WiredVariableModule(1, directory, store, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var holders = Enumerable.Range(1, 200).Select(id => new WiredVariableHolder(WiredVariableTarget.User, 10000 + id, id)).ToArray();
        var frame = new WiredVariableFrame(1, holders);
        var bindings = Enumerable.Range(10, 6).Select(id => new WiredVariableFxBinding(
            new(id, true, 0, 3000, 0, 0, 0, 0, 0, 0, 100, ImmutableSortedDictionary<string, string>.Empty),
            new(WiredVariableTarget.User, $"custom:{id}"), 2, null, 0, null, null)).ToArray();

        foreach (var binding in bindings) {
            foreach (var holder in holders) {
                store.Values.Mutate(new((uint)binding.Config.Id, holder.Target, holder.StableId), _ => new(25, DateTimeOffset.FromUnixTimeMilliseconds(1000), DateTimeOffset.FromUnixTimeMilliseconds(1000)));
            }
        }

        // The previous holder loop's reads: 1,200 scalar SQL reads and 3,600 directory lookups.
        foreach (var binding in bindings) {
            foreach (var holder in holders) {
                Assert.NotNull(module.Read(binding.Variable, holder, frame));
            }
        }

        Assert.Equal(1200, store.ScalarReads);
        Assert.Equal(1200, directory.DefinitionReads);
        Assert.Equal(2400, directory.OwnerReads);
        store.ScalarReads = 0;
        directory.DefinitionReads = 0;
        directory.OwnerReads = 0;

        var tracker = new WiredVariableFxTracker(module, 2000);

        using (var reads = tracker.CaptureReads(frame, bindings)) {
            var first = tracker.Update(holders[0], frame, bindings, holders, _ => 0, reads);
            var second = tracker.Update(holders[1], frame, bindings, holders, _ => 0, reads);
            Assert.Equal(1200, first.Statuses.Count);
            Assert.Equal(1200, second.Statuses.Count);
            Assert.Equal(0, store.ScalarReads);
            Assert.Equal(6, store.BulkDefinitionReads);
            Assert.Equal(6, directory.DefinitionReads);
            Assert.Equal(1, directory.OwnerReads);
        }
    }

    [Fact]
    public void NextFlushRechecksReferenceConfigurationOwnerAndPlacement()
    {
        var directory = new Directory();
        var store = new CountingStore();
        var module = new WiredVariableModule(1, directory, store, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var holder = new WiredVariableHolder(WiredVariableTarget.User, 900, 7);
        var frame = new WiredVariableFrame(1, [holder]);
        var reference = new WiredVariableReference(holder.Target, "custom:10");
        store.Values.Mutate(new(10, holder.Target, holder.StableId), _ => new(10, DateTimeOffset.FromUnixTimeMilliseconds(1000), DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        store.Values.Mutate(new(11, holder.Target, holder.StableId), _ => new(11, DateTimeOffset.FromUnixTimeMilliseconds(1000), DateTimeOffset.FromUnixTimeMilliseconds(1000)));

        using (var reads = module.CaptureReads([reference], frame)) {
            Assert.Equal(10, reads.Read(reference, holder, frame)!.Value);
        }

        directory.Link = new(1, new(holder.Target, "custom:11"), true);

        using (var reads = module.CaptureReads([reference], frame)) {
            Assert.Equal(11, reads.Read(reference, holder, frame)!.Value);
        }

        directory.Owner = 6;

        using (var reads = module.CaptureReads([reference], frame)) {
            Assert.Null(reads.Read(reference, holder, frame));
        }

        directory.Owner = 5;
        directory.Deleted = true;

        using (var reads = module.CaptureReads([reference], frame)) {
            Assert.Null(reads.Read(reference, holder, frame));
        }

        var disposed = module.CaptureReads([reference], frame);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => disposed.Read(reference, holder, frame));
    }

    private sealed class Directory : IWiredVariableDirectory
    {
        public int DefinitionReads, OwnerReads;
        public uint Owner = 5;
        public bool Deleted;
        public WiredVariableLink? Link;
        public WiredVariableDefinition? Find(uint id)
        {
            DefinitionReads++;

            return Deleted || id is < 10 or > 15 ? null : new(id, 1, 5, $"v{id}", WiredVariableTarget.User,
                WiredVariableAvailability.Persistent, true, Link: id == 10 ? Link : null);
        }
        public uint? GetRoomOwner(uint roomId)
        {
            OwnerReads++;

            return roomId == 1 ? Owner : null;
        }
    }
    private sealed class CountingStore : IWiredVariableStore
    {
        public readonly MemoryWiredVariableStore Values = new();
        public int ScalarReads, BulkDefinitionReads;
        public WiredVariableValue? Read(WiredVariableKey key)
        {
            ScalarReads++;

            return Values.Read(key);
        }
        public WiredVariableWrite Mutate(WiredVariableKey key, Func<WiredVariableValue?, WiredVariableValue?> update,
            WiredVariableAuthorization? authorization = null) => Values.Mutate(key, update, authorization);
        public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetHolders(uint definitionId)
        {
            BulkDefinitionReads++;

            return Values.GetHolders(definitionId);
        }
        public int DeleteDefinition(uint definitionId) => Values.DeleteDefinition(definitionId);
    }
}
