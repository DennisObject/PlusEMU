using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableTests
{
    private sealed class Directory : IWiredVariableDirectory
    {
        public Dictionary<uint, WiredVariableDefinition> Definitions { get; } = [];
        public Dictionary<uint, uint> Owners { get; } = new() { [1] = 5, [2] = 5 };
        public WiredVariableDefinition? Find(uint id) => Definitions.GetValueOrDefault(id);
        public uint? GetRoomOwner(uint id) => Owners.TryGetValue(id, out var owner) ? owner : null;
    }
    private static readonly WiredVariableReference UserRef = new(WiredVariableTarget.User, "custom:10");
    private static WiredVariableDefinition User(WiredVariableAvailability availability = WiredVariableAvailability.Persistent) =>
        new(10, 1, 5, "score", WiredVariableTarget.User, availability, true);
    private static WiredVariableHolder Holder(long stable, int entity) => new(WiredVariableTarget.User, stable, entity);
    private static WiredVariableFrame Frame(params WiredVariableHolder[] holders) => new(1, holders);

    [Fact]
    public void TwoTemporaryItemsKeepIndependentActiveValuesAndNeverReachDurableStorage()
    {
        var directory = new Directory();
        directory.Definitions[10] = new(10, 1, 5, "active", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true);
        directory.Definitions[11] = directory.Definitions[10] with { ItemId = 11, Availability = WiredVariableAvailability.Persistent };
        var items = new[] { new Plus.HabboHotel.Items.Item { Id = uint.MaxValue, IsTemporary = true },
            new Plus.HabboHotel.Items.Item { Id = uint.MaxValue - 1, IsTemporary = true } };
        var a = WiredVariableRuntimeFrames.FurniHolder(items[0]); var b = WiredVariableRuntimeFrames.FurniHolder(items[1]);
        Assert.Equal(0, a.StableId); Assert.Equal(0, b.StableId); Assert.Equal(-1, a.EntityId); Assert.Equal(-2, b.EntityId);
        var durable = new MemoryWiredVariableStore(); var module = new WiredVariableModule(1, directory, durable, () => 1);
        var frame = Frame(a, b); var active = new WiredVariableReference(WiredVariableTarget.Furni, "custom:10");
        var persistent = new WiredVariableReference(WiredVariableTarget.Furni, "custom:11");
        Assert.True(module.Mutate(active, a, WiredVariableMutation.Give, 10, frame));
        Assert.True(module.Mutate(active, b, WiredVariableMutation.Give, 20, frame));
        using (var reads = module.CaptureReads([active], frame))
        { Assert.Equal(10, reads.Read(active, a, frame)!.Value); Assert.Equal(20, reads.Read(active, b, frame)!.Value); }
        foreach (var holder in frame.Holders)
        {
            Assert.False(module.Mutate(persistent, holder, WiredVariableMutation.Give, 99, frame));
            Assert.Null(module.Read(persistent, holder, frame));
        }
        Assert.Empty(durable.GetHolders(11)); Assert.Equal(2, module.DrainChanges().Count);
        module.HolderLeft(a);
        Assert.Null(module.Read(active, a, frame)); Assert.Equal(20, module.Read(active, b, frame)!.Value);
        module.HolderLeft(b); Assert.Null(module.Read(active, b, frame));
        Assert.False(module.Mutate(active, a, WiredVariableMutation.Give, 99, Frame(b)));
    }

    [Fact]
    public void PersistentUserSurvivesNewRoomIndexWithoutLeakingToItsNextOccupant()
    {
        var directory = new Directory(); directory.Definitions[10] = User();
        var durable = new MemoryWiredVariableStore();
        var first = new WiredVariableModule(1, directory, durable, () => 1000);
        var alice = Holder(100, 1);
        Assert.True(first.Mutate(UserRef, alice, WiredVariableMutation.Give, 23, Frame(alice)));
        first.HolderLeft(alice);
        first.DetachDefinition(10); // Pickup preserves durable data.
        var loaded = new WiredVariableModule(1, directory, durable, () => 2000);
        var aliceReturned = Holder(100, 20); var bob = Holder(200, 1);
        Assert.Equal(23, loaded.Read(UserRef, aliceReturned, Frame(aliceReturned, bob))!.Value);
        Assert.Null(loaded.Read(UserRef, bob, Frame(aliceReturned, bob)));
        Assert.Equal(1, loaded.DeleteDefinition(10));
        Assert.Null(loaded.Read(UserRef, aliceReturned, Frame(aliceReturned)));
    }

    [Fact]
    public void ActiveValueEndsAtDepartureAndNewRoomModule()
    {
        var directory = new Directory(); directory.Definitions[10] = User(WiredVariableAvailability.UserActive);
        var durable = new MemoryWiredVariableStore(); var holder = Holder(7, 12);
        var module = new WiredVariableModule(1, directory, durable, () => 1);
        Assert.True(module.Mutate(UserRef, holder, WiredVariableMutation.Give, 4, Frame(holder)));
        Assert.Empty(durable.GetHolders(10));
        module.HolderLeft(holder);
        Assert.Null(module.Read(UserRef, holder, Frame(holder)));
    }

    [Fact]
    public void CreationUpdateRemovalHaveActualTimestampsAndNoOpWritesEmitNothing()
    {
        var directory = new Directory(); directory.Definitions[10] = User();
        long time = 100; var holder = Holder(7, 42);
        var module = new WiredVariableModule(1, directory, new MemoryWiredVariableStore(), () => time);
        var frame = Frame(holder);
        Assert.True(module.Mutate(UserRef, holder, WiredVariableMutation.Give, 0, frame));
        Assert.False(module.Mutate(UserRef, holder, WiredVariableMutation.Give, 9, frame));
        time = 200;
        Assert.True(module.Change(UserRef, holder, WiredVariableMutation.Set, value => value + 3, frame));
        Assert.False(module.Mutate(UserRef, holder, WiredVariableMutation.Set, 3, frame));
        var value = module.Read(UserRef, holder, frame)!;
        Assert.Equal(100, value.CreatedAtMs); Assert.Equal(200, value.UpdatedAtMs);
        Assert.True(module.Mutate(UserRef, holder, WiredVariableMutation.Remove, 0, frame));
        Assert.Equal(new[] { WiredVariableChangeKind.Created, WiredVariableChangeKind.Updated, WiredVariableChangeKind.Removed }, module.DrainChanges().Select(x => x.Kind));
        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public void ContextBelongsToFrameAndCanExplicitlyBeInheritedBySignal()
    {
        var directory = new Directory(); directory.Definitions[11] = new(11, 1, 5, "capture", WiredVariableTarget.Context, WiredVariableAvailability.RoomActive, true);
        var module = new WiredVariableModule(1, directory, new MemoryWiredVariableStore(), () => 1);
        var reference = new WiredVariableReference(WiredVariableTarget.Context, "custom:11");
        var holder = new WiredVariableHolder(WiredVariableTarget.Context, 0, 0);
        var frame = Frame(); var unrelated = Frame();
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Give, 17, frame));
        Assert.Null(module.Read(reference, holder, unrelated));
        var child = new WiredVariableFrame(1, []) { Context = frame.Context };
        Assert.Equal(17, module.Read(reference, holder, child)!.Value);
    }

    [Fact]
    public void SharedReferenceResolvesByIdEnforcesReadOnlyAndRechecksOwnership()
    {
        var directory = new Directory();
        directory.Definitions[20] = User(WiredVariableAvailability.Shared) with { ItemId = 20, RoomId = 2 };
        directory.Definitions[10] = User() with { Link = new(2, new(WiredVariableTarget.User, "custom:20"), true) };
        var holder = Holder(11, 1); var frame = Frame(holder); var store = new MemoryWiredVariableStore();
        store.Mutate(new(20, WiredVariableTarget.User, 11), _ => new(91, 1, 1));
        var module = new WiredVariableModule(1, directory, store, () => 2);
        Assert.Equal(91, module.Read(UserRef, holder, frame)!.Value);
        Assert.False(module.Mutate(UserRef, holder, WiredVariableMutation.Set, 8, frame));
        Assert.Empty(module.DrainChanges());
        directory.Definitions[10] = directory.Definitions[10] with { Link = directory.Definitions[10].Link! with { ReadOnly = false } };
        Assert.True(module.Mutate(UserRef, holder, WiredVariableMutation.Set, 8, frame));
        Assert.Equal(8, store.Read(new(20, WiredVariableTarget.User, 11))!.Value);
        directory.Owners[2] = 99;
        Assert.Null(module.Read(UserRef, holder, frame));
        Assert.False(module.Mutate(UserRef, holder, WiredVariableMutation.Remove, 0, frame));
    }

    [Fact]
    public void EchoCyclesAndForgedHolderIdentitiesAreRejected()
    {
        var directory = new Directory(); directory.Definitions[10] = User() with { Link = new(1, new(WiredVariableTarget.User, "custom:11"), false) };
        directory.Definitions[11] = User() with { ItemId = 11, Link = new(1, UserRef, false) };
        var holder = Holder(5, 1); var module = new WiredVariableModule(1, directory, new MemoryWiredVariableStore(), () => 1);
        Assert.Null(module.Read(UserRef, holder, Frame(holder)));
        Assert.False(module.Mutate(UserRef, holder, WiredVariableMutation.Give, 1, Frame(holder)));
        directory.Definitions[10] = User();
        Assert.False(module.Mutate(UserRef, Holder(99, 1), WiredVariableMutation.Give, 9, Frame(holder)));
    }

    [Fact]
    public void SameNamesDoNotAliasAndPresenceOnlyValuesCannotBeWritten()
    {
        var directory = new Directory(); directory.Definitions[10] = User() with { HasValue = false };
        directory.Definitions[11] = User() with { ItemId = 11 };
        var holder = Holder(1, 1); var frame = Frame(holder);
        var module = new WiredVariableModule(1, directory, new MemoryWiredVariableStore(), () => 1);
        Assert.True(module.Mutate(UserRef, holder, WiredVariableMutation.Give, 444, frame));
        Assert.Equal(1, module.Read(UserRef, holder, frame)!.Value);
        Assert.False(module.Mutate(UserRef, holder, WiredVariableMutation.Set, 6, frame));
        Assert.Null(module.Read(new(WiredVariableTarget.User, "custom:11"), holder, frame));
    }

    [Fact]
    public void RoomVariableStartsAvailableAndFirstUpdatePersistsAcrossReload()
    {
        var directory = new Directory(); directory.Definitions[10] = new(10, 1, 5, "global", WiredVariableTarget.Global, WiredVariableAvailability.Persistent, true, 7);
        var store = new MemoryWiredVariableStore(); var module = new WiredVariableModule(1, directory, store, () => 3);
        var reference = new WiredVariableReference(WiredVariableTarget.Global, "custom:10");
        var holder = new WiredVariableHolder(WiredVariableTarget.Global, 0, 0);
        Assert.Equal(7, module.Read(reference, holder, Frame())!.Value);
        Assert.True(module.Change(reference, holder, WiredVariableMutation.Set, value => value + 1, Frame()));
        var loaded = new WiredVariableModule(1, directory, store, () => 4);
        Assert.Equal(8, loaded.Read(reference, holder, Frame())!.Value);
    }

    [Fact]
    public void RoomVariableEditorReopensCurrentDurableValueAndSeedDoesNotOverwriteIt()
    {
        var directory = new Directory(); directory.Definitions[10] = new(10, 1, 5, "global", WiredVariableTarget.Global, WiredVariableAvailability.Persistent, true, 7);
        var store = new MemoryWiredVariableStore(); var module = new WiredVariableModule(1, directory, store, () => 1000);
        var saved = new WiredConfiguration { IntParams = [10, 7], Text = "global" };
        Assert.True(module.InitializeGlobal(10));
        var editor = new WiredVariableEditor(module);
        Assert.True(editor.SaveGlobalValue(10, 42));
        module.DrainChanges();
        Assert.True(editor.SaveGlobalValue(10, 42));
        Assert.Empty(module.DrainChanges());
        var reloaded = new WiredVariableModule(1, directory, store, () => 2000);
        Assert.True(reloaded.InitializeGlobal(10));
        Assert.Equal(42, new WiredVariableEditor(reloaded).ForDisplay("wf_var_room", 10, saved).IntParams[1]);
        Assert.Equal(7, saved.IntParams[1]);
        directory.Owners[1] = 6;
        Assert.False(editor.SaveGlobalValue(10, 99));
        Assert.Equal(42, store.Read(new(10, WiredVariableTarget.Global, 0))!.Value);
    }

    [Fact]
    public void FailedCommitDoesNotPublishChange()
    {
        var directory = new Directory(); directory.Definitions[10] = User();
        var holder = Holder(1, 1);
        var module = new WiredVariableModule(1, directory, new FailingStore(), () => 1);
        Assert.Throws<IOException>(() => module.Mutate(UserRef, holder, WiredVariableMutation.Give, 3, Frame(holder)));
        Assert.Empty(module.DrainChanges());
    }

    [Fact]
    public void ReadModifyWriteIsAtomicAcrossModulesSharingDurableStore()
    {
        var directory = new Directory(); directory.Definitions[10] = User();
        var store = new MemoryWiredVariableStore(); var holder = Holder(1, 1); var frame = Frame(holder);
        var modules = Enumerable.Range(0, 4).Select(_ => new WiredVariableModule(1, directory, store, () => 1)).ToArray();
        modules[0].Mutate(UserRef, holder, WiredVariableMutation.Give, 0, frame);
        Parallel.For(0, 400, i => modules[i % 4].Change(UserRef, holder, WiredVariableMutation.Set, value => value + 1, frame));
        Assert.Equal(400, modules[0].Read(UserRef, holder, frame)!.Value);
    }

    [Theory]
    [InlineData(1, int.MaxValue, 1, int.MaxValue)]
    [InlineData(4, int.MinValue, -1, int.MaxValue)]
    [InlineData(4, 42, 0, 42)]
    [InlineData(40, 8, 3, 3)]
    [InlineData(41, 8, 3, 8)]
    [InlineData(110, -1, 0, 32)]
    [InlineData(112, 16, 0, 4)]
    [InlineData(120, 16, 4, -1)]
    public void OctaneArithmeticHasSaturatedMathAndBitScans(int operation, int current, int operand, int expected) =>
        Assert.Equal(expected, WiredVariableArithmetic.Apply(operation, current, operand));

    [Fact]
    public void DefinitionDecoderUsesOctaneUserSlotOrder()
    {
        Assert.True(WiredVariableDefinitions.TryDecode("wf_var_user", 10, 1, 5, new() { IntParams = [1, 10], Text = "score" }, out var definition, out _));
        Assert.True(definition!.HasValue); Assert.True(definition.IsDurable);
        Assert.False(WiredVariableDefinitions.TryDecode("wf_var_user", 10, 1, 5, new() { IntParams = [10, 1], Text = "score" }, out _, out _));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("reference")]
    [InlineData("delete")]
    public void TransactionRechecksAuthorizationAfterResolution(string change)
    {
        var directory = new Directory();
        directory.Definitions[10] = User() with { Link = new(2, new(WiredVariableTarget.User, "custom:20"), false) };
        directory.Definitions[20] = User(WiredVariableAvailability.Shared) with { ItemId = 20, RoomId = 2 };
        var backing = new MemoryWiredVariableStore();
        var store = new AuthorizingStore(backing, directory, () =>
        {
            if (change == "owner") directory.Owners[2] = 8;
            else if (change == "delete") directory.Definitions.Remove(20);
            else directory.Definitions[10] = directory.Definitions[10] with { Link = directory.Definitions[10].Link! with { ReadOnly = true } };
        });
        var module = new WiredVariableModule(1, directory, store, () => 1);
        var holder = Holder(1, 2);
        Assert.False(module.Mutate(UserRef, holder, WiredVariableMutation.Give, 3, Frame(holder)));
        Assert.Empty(backing.GetHolders(20));
        Assert.Empty(module.DrainChanges());
    }

    private sealed class AuthorizingStore(IWiredVariableStore backing, IWiredVariableDirectory directory, Action beforeCommit) : IWiredVariableStore
    {
        public WiredVariableValue? Read(WiredVariableKey key) => backing.Read(key);
        public WiredVariableWrite Mutate(WiredVariableKey key, Func<WiredVariableValue?, WiredVariableValue?> update, WiredVariableAuthorization? authorization = null)
        {
            beforeCommit();
            return authorization?.IsCurrent(directory) == true ? backing.Mutate(key, update) : new(null, null);
        }
        public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetHolders(uint definitionId) => backing.GetHolders(definitionId);
        public int DeleteDefinition(uint definitionId) => backing.DeleteDefinition(definitionId);
    }

    private sealed class FailingStore : IWiredVariableStore
    {
        public WiredVariableValue? Read(WiredVariableKey key) => null;
        public WiredVariableWrite Mutate(WiredVariableKey key, Func<WiredVariableValue?, WiredVariableValue?> update, WiredVariableAuthorization? authorization = null) => throw new IOException("write rejected");
        public IReadOnlyDictionary<WiredVariableKey, WiredVariableValue> GetHolders(uint definitionId) => new Dictionary<WiredVariableKey, WiredVariableValue>();
        public int DeleteDefinition(uint definitionId) => throw new IOException("write rejected");
    }
}
