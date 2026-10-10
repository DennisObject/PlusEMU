using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;
using static Plus.Tests.WiredSelectorTests;

namespace Plus.Tests;

public sealed class WiredSelectorVariableBridgeTests
{
    [Fact]
    public void SelectorsUseRoomIdsButReadStablePlayerAndFurnitureHoldersAndDisposeTheirSnapshot()
    {
        var user = new WiredVariableHolder(WiredVariableTarget.User, 999, 4);
        var furniture = new WiredVariableHolder(WiredVariableTarget.Furni, 3, 3);
        var frame = new WiredVariableFrame(1, [user, furniture]);
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        module.Mutate(new(user.Target, "custom:10"), user, WiredVariableMutation.Give, 5, frame);
        module.Mutate(new(furniture.Target, "custom:11"), furniture, WiredVariableMutation.Give, 8, frame);
        var queries = WiredSelectorVariableBridge.Create(module, frame);
        var inputs = Inputs() with { FurniVariablePredicate = queries.FurniPredicate, UserVariablePredicate = queries.UserPredicate };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_users_with_var", Var("wf_slc_users_with_var", null, "custom:10"), World(), inputs);
        Assert.Equal(new[] { 4 }, selected.Selection.UserIds);
        Assert.False(queries.UserPredicate("wf_slc_users_with_var", Var("wf_slc_users_with_var", [0, 2, 0, 0, 0, 0, 0, 0, 0], "custom:10"), 999));
        Assert.Equal(new uint[] { 3 }, WiredSelectorModule.SelectRaw("wf_slc_furni_with_var", Var("wf_slc_furni_with_var", null, "custom:11"), World(), inputs).Selection.FurniIds);
        Assert.Equal(8, queries.ReadOperand(new(1, "furni:11", 0, 100, Config(picks: [3]))));
        queries.Dispose();
        Assert.Throws<ObjectDisposedException>(() => queries.ReadOperand(new(1, "furni:11", 0, 100, Config(picks: [3]))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemoteVariableOperandReadsItsReferencedStackPoolIncludingNestedStacks(bool nested)
    {
        var holders = Enumerable.Range(1, 3).Select(id => new WiredVariableHolder(WiredVariableTarget.Furni, id, id)).ToArray();
        var frame = new WiredVariableFrame(1, holders);
        frame.Selector.Add(holders[2]); // Caller selector pool intentionally differs from the referenced stack.
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), TimeProvider.System);

        foreach (var holder in holders) {
            Assert.True(module.Mutate(new(holder.Target, "custom:11"), holder, WiredVariableMutation.Give, holder.EntityId == 3 ? 99 : 10, frame));
            Assert.True(module.Mutate(new(holder.Target, "custom:12"), holder, WiredVariableMutation.Give, holder.EntityId == 3 ? 99 : 10, frame));
        }

        var compare = Var("wf_slc_furni_with_var", [1, 2, 1, 0, 1, 0, 200, 0, 0], "custom:11\tcustom:12");
        WiredSelectorFurniture Box(uint id, int x, double z) => new(id, 1, "Wired", "0", x, 8, z, 1, [(x, 8)], IsWired: true);
        var world = World() with
        {
            Furni = [.. World().Furni, Box(101, 8, 0), Box(102, 8, 1), Box(103, 9, 0), Box(201, 7, 0), Box(202, 7, 1)],
            RemoteSelectors = new Dictionary<uint, WiredRemoteSelector>
            {
                [101] = new("wf_slc_furni_picks", Config(picks: [1])),
                [102] = new("wf_slc_furni_with_var", compare),
                [103] = new("wf_slc_remote", Config([0, 0, 0, 0, 100], [101])),
                [201] = new("wf_slc_furni_picks", Config(picks: [3])),
                [202] = new("wf_slc_furni_with_var", compare)
            }
        };
        using var queries = WiredSelectorVariableBridge.Create(module, frame);
        var outer = new WiredSelectedIds();
        outer.FurniIds.Add(3);
        var input = Inputs() with
        {
            SelectorPool = outer,
            FurniModified = true,
            FurniVariablePredicate = queries.FurniPredicate,
            UserVariablePredicate = queries.UserPredicate,
            ScopedFurniVariablePredicate = queries.ScopedFurniPredicate,
            ScopedUserVariablePredicate = queries.ScopedUserPredicate
        };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_remote", Config([0, 0, 0, 0, 100], [nested ? 103u : 101u]), world, input);
        Assert.Equal(new uint[] { 1, 2 }, selected.Selection.FurniIds);
        // A second stack has a separate operand snapshot even when its configuration is identical.
        var other = WiredSelectorModule.SelectRaw("wf_slc_remote", Config([0, 0, 0, 0, 100], [201]), world, input);
        Assert.Equal(new uint[] { 3 }, other.Selection.FurniIds);
        Assert.Equal(new uint[] { 3 }, outer.FurniIds);
    }

    [Fact]
    public void RemoteVariableOperandUsesReferencedSelectorOrderRatherThanRoomHolderOrder()
    {
        var first = new WiredVariableHolder(WiredVariableTarget.Furni, 1, 1);
        var second = new WiredVariableHolder(WiredVariableTarget.Furni, 2, 2);
        var frame = new WiredVariableFrame(1, [first, second]);
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), TimeProvider.System);

        foreach (var holder in frame.Holders) {
            Assert.True(module.Mutate(new(holder.Target, "custom:11"), holder, WiredVariableMutation.Give, holder.EntityId * 10, frame));
            Assert.True(module.Mutate(new(holder.Target, "custom:12"), holder, WiredVariableMutation.Give, holder.EntityId * 10, frame));
        }

        var world = World() with
        {
            RemoteSelectors = new Dictionary<uint, WiredRemoteSelector>
            {
                [101] = new("wf_slc_furni_picks", Config(picks: [2])),
                [102] = new("wf_slc_furni_picks", Config(picks: [1])),
                [103] = new("wf_slc_furni_with_var", Var("wf_slc_furni_with_var", [1, 2, 1, 0, 1, 0, 200, 1, 0], "custom:11\tcustom:12"))
            },
            Furni = [.. World().Furni,
                new(101, 1, "first selector", "", 8, 8, 0, 1, [(8, 8)], IsWired: true),
                new(102, 1, "second selector", "", 8, 8, 1, 1, [(8, 8)], IsWired: true),
                new(103, 1, "variable filter", "", 8, 8, 2, 1, [(8, 8)], IsWired: true)]
        };
        using var queries = WiredSelectorVariableBridge.Create(module, frame);
        var input = Inputs() with
        {
            ScopedFurniVariablePredicate = queries.ScopedFurniPredicate,
            ScopedUserVariablePredicate = queries.ScopedUserPredicate
        };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_remote", Config([0, 0, 0, 0, 100], [101]), world, input);
        Assert.Equal(new uint[] { 2 }, selected.Selection.FurniIds);
    }

    private static WiredConfiguration Var(string selector, int[]? fields, string text) =>
        WiredVariableFixtures.WithVar(selector, Config(fields ?? [0, 0, 0, 0, 0, 0, 0, 0, 0], text: text));

    private sealed class Directory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint itemId) => itemId is 10 or 11 or 12
            ? new(itemId, 1, 5, "test", itemId == 10 ? WiredVariableTarget.User : WiredVariableTarget.Furni,
                WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => 5;
    }
}
