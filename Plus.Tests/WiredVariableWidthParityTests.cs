using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableWidthParityTests
{
    [Theory]
    [InlineData(1, 2147483647, 1, 2147483648L)]
    [InlineData(5, 2, 63, long.MinValue)]
    [InlineData(5, 2, 64, 0L)]
    [InlineData(104, 1, 63, long.MinValue)]
    [InlineData(104, 1, 64, 1L)]
    [InlineData(105, -1, 1, -1L)]
    [InlineData(5, 2, -1, 0L)]
    public void ArithmeticMatchesObservedSignedSixtyFourBitOperations(int operation, int current, int operand, long expected)
    {
        Assert.Equal(expected, WiredVariableArithmetic.Apply(operation, current, operand));
    }

    [Theory]
    [InlineData(1L, -1L, 1L)]
    [InlineData(-1L, -1L, -1L)]
    [InlineData(-1L, -2L, 1L)]
    [InlineData(1L, -2L, 1L)]
    [InlineData(0L, -1L, 0L)]
    [InlineData(-2L, -1L, 0L)]
    [InlineData(0L, 0L, 1L)]
    [InlineData(-1L, long.MinValue, 1L)]
    public void NegativePowerPreservesUnitBasesWithoutNegatingTheExponent(long number, long exponent, long expected)
    {
        Assert.Equal(expected, WiredVariableArithmetic.Apply(5, number, exponent));
    }

    [Theory]
    [InlineData(4, -1, long.MinValue)]
    [InlineData(4, 0, long.MinValue)]
    [InlineData(6, -1, 0L)]
    [InlineData(60, 0, long.MinValue)]
    public void SignedMinimumOperationsMatchObservedWrapAndZeroDivisorBehavior(int operation, int operand, long expected)
    {
        Assert.Equal(expected, WiredVariableArithmetic.Apply(operation, long.MinValue, operand));
    }

    [Theory]
    [InlineData(WiredVariableTarget.Context, false)]
    [InlineData(WiredVariableTarget.Context, true)]
    [InlineData(WiredVariableTarget.Global, false)]
    [InlineData(WiredVariableTarget.Global, true)]
    [InlineData(WiredVariableTarget.User, false)]
    [InlineData(WiredVariableTarget.User, true)]
    [InlineData(WiredVariableTarget.Furni, false)]
    [InlineData(WiredVariableTarget.Furni, true)]
    public void ScalarValuesRemainExactBeyondJavascriptPrecisionThroughBatchAndNotifications(WiredVariableTarget target, bool batched)
    {
        var holder = target is WiredVariableTarget.Context or WiredVariableTarget.Global ? new(target, 0, 0) : new WiredVariableHolder(target, 1, 1);
        var frame = new WiredVariableFrame(1, [holder]) { Trigger = [holder], VariableChanges = batched ? new() : null };
        var module = new WiredVariableModule(1, new Directory(target), new MemoryWiredVariableStore(), TimeProvider.System);
        Assert.True(module.Mutate(new(target, "custom:10"), holder, target == WiredVariableTarget.Global ? WiredVariableMutation.Set : WiredVariableMutation.Give, 2, frame));
        module.DrainChanges();
        var executor = new WiredVariableExecutors(module, TimeProvider.System);
        WiredConfiguration Config(int operation, int operand) => new() { IntParams = [(int)target, operation, 0, operand, (int)target, 0, 0, 0, 0], Text = "custom:10" };
        Assert.True(executor.Execute("wf_act_change_var_val", WiredNativeTestSupport.Scalar("wf_act_change_var_val", Config(5, 53)), frame));
        Assert.True(executor.Execute("wf_act_change_var_val", WiredNativeTestSupport.Scalar("wf_act_change_var_val", Config(1, 1)), frame));

        if (batched) {
            Assert.True(frame.VariableChanges!.Flush());
        }

        Assert.Equal(9007199254740993L, module.Read(new(target, "custom:10"), holder, frame)!.Value);
        var changes = module.DrainChanges();
        Assert.Equal(batched ? 1 : 2, changes.Count);
        Assert.Equal(9007199254740993L, changes.Last().After!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WideReferenceOperandsAndAliasesRemainExactForEverySelectedTarget(bool batched)
    {
        var holders = new[] { new WiredVariableHolder(WiredVariableTarget.Furni, 1, 1), new WiredVariableHolder(WiredVariableTarget.Furni, 2, 2) };
        var frame = new WiredVariableFrame(1, holders) { Trigger = holders, VariableChanges = batched ? new() : null };
        var module = new WiredVariableModule(1, new Directory(WiredVariableTarget.Furni), new MemoryWiredVariableStore(), TimeProvider.System);

        foreach (var (holder, index) in holders.Select((holder, index) => (holder, index))) {
            Assert.True(module.Mutate(new(holder.Target, "custom:10"), holder, WiredVariableMutation.Give, index, frame));
            Assert.True(module.Mutate(new(holder.Target, "custom:11"), holder, WiredVariableMutation.Give, index == 0 ? long.MaxValue : 2, frame));
        }

        module.DrainChanges();
        var executor = new WiredVariableExecutors(module, TimeProvider.System);
        Assert.True(executor.Execute("wf_act_change_var_val", WiredNativeTestSupport.Scalar("wf_act_change_var_val", new() { IntParams = [1, 1, 1, 0, 1, 0, 0, 0, 0], Text = "custom:12\tcustom:11" }), frame));

        if (batched) {
            Assert.True(frame.VariableChanges!.Flush());
        }

        Assert.Equal(new[] { long.MaxValue, long.MinValue }, holders.Select(holder => module.Read(new(holder.Target, "custom:10"), holder, frame)!.Value));
        Assert.Equal(new[] { long.MaxValue, long.MinValue }, module.DrainChanges().Select(change => change.After!.Value));
        using var query = new WiredVariableQueries(module, frame);
        var match = WiredVariableFixtures.WithVar("wf_slc_furni_with_var", new() { IntParams = [1, 2, 1, 0, 1, 0, 0, 0, 0], Text = "custom:12\tcustom:11" });
        Assert.True(query.MatchSelector("wf_slc_furni_with_var", match, holders[0]));
        Assert.False(query.MatchSelector("wf_slc_furni_with_var", match, holders[1]));
        Assert.True(WiredVariablePredicates.Compare(0, long.MaxValue, 9007199254740993L));
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(9007199254740993L)]
    public void MemoryStorageRoundTripsExactSignedValues(long number)
    {
        var holder = new WiredVariableHolder(WiredVariableTarget.Global, 0, 0);
        var frame = new WiredVariableFrame(1, []);
        var store = new MemoryWiredVariableStore();
        var key = new WiredVariableKey(10, WiredVariableTarget.Global, 0);
        store.Mutate(key, _ => new(number, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        Assert.Equal(number, ((IWiredVariableStore)store).ReadMany([key])[key].Value);
        var module = new WiredVariableModule(1, new Directory(WiredVariableTarget.Global), store, TimeProvider.System);
        Assert.True(module.Mutate(new(holder.Target, "custom:10"), holder, WiredVariableMutation.Set, number, frame));
        Assert.Equal(number, module.Read(new(WiredVariableTarget.Global, "custom:10"), new(WiredVariableTarget.Global, 0, 0), frame)!.Value);
    }

    [Fact]
    public void RoomDefinitionKeepsItsAvailabilityAndAlwaysStartsAtZero()
    {
        Assert.True(WiredVariableDefinitions.TryDecode("wf_var_room", 10, 1, 5,
            new() { IntParams = [10, 0], Text = "native" }, out var definition, out _));
        Assert.Equal(WiredVariableAvailability.Persistent, definition!.Availability);
        Assert.Equal(0, definition.InitialValue);
        Assert.False(WiredVariableDefinitions.TryDecode("wf_var_room", 10, 1, 5,
            new() { IntParams = [10, -7], Text = "initial" }, out _, out _));
    }

    [Theory]
    [InlineData(new[] { 10, 1, 0 })]
    [InlineData(new[] { 10, 1, 0, 7, 0 })]
    [InlineData(new[] { 10, 2, 0, 7 })]
    [InlineData(new[] { 0, 1, 0, 7 })]
    public void RoomWideDefinitionRejectsMalformedShapeAndVersion(int[] fields)
    {
        Assert.False(WiredVariableDefinitions.TryDecode("wf_var_room", 10, 1, 5,
            new() { IntParams = [.. fields], Text = "invalid" }, out _, out _));
    }

    [Fact]
    public void WideCustomWritesAreRejectedBeforeBoundedNativePropertiesReceiveThem()
    {
        var holder = new WiredVariableHolder(WiredVariableTarget.Furni, 1, 1);
        var frame = new WiredVariableFrame(1, [holder]);
        var native = new NativeProperty();
        var module = new WiredVariableModule(1, new Directory(holder.Target), new MemoryWiredVariableStore(), TimeProvider.System, native);
        Assert.False(module.Mutate(new(holder.Target, "internal:@state"), holder, WiredVariableMutation.Set, 2147483648L, frame));
        Assert.Equal(0, native.Writes);
        Assert.Empty(module.DrainChanges());
    }

    private sealed class NativeProperty : IWiredBuiltinVariables
    {
        public int Writes { get; private set; }
        public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame) => new(1, null, null);
        public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame)
        {
            Writes++;

            return true;
        }
    }

    private sealed class Directory(WiredVariableTarget target) : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id switch
        {
            10 or 11 => new(id, 1, 5, "wide" + id, target, WiredVariableAvailability.RoomActive, true),
            12 => new(id, 1, 5, "alias", target, WiredVariableAvailability.RoomActive, true, Link: new(1, new(target, "custom:10"), false)),
            _ => null
        };
        public uint? GetRoomOwner(uint roomId) => 5;
    }
}
