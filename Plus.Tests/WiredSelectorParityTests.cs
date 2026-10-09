using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Xunit;

namespace Plus.Tests;

public sealed class WiredSelectorParityTests
{
    [Fact]
    public void GroupSelectorMatchesEquippedGroupRatherThanAllMemberships()
    {
        var world = new WiredSelectorWorld(2, 2, [],
        [new(1, "equipped", WiredSelectorEntityKind.Player, 0, 0, GroupIds: new HashSet<int> { 9, 10 }, EquippedGroupId: 9),
         new(2, "member with different badge", WiredSelectorEntityKind.Player, 0, 0, GroupIds: new HashSet<int> { 9, 10 }, EquippedGroupId: 10)], RoomGroupId: 9);
        var selected = WiredSelectorModule.SelectRaw("wf_slc_users_group", new() { IntParams = [0, 0, 0, 0] },
            world, new(new(), new(), new()));
        Assert.Equal(new[] { 1 }, selected.Selection.UserIds);
    }

    [Theory]
    [InlineData(0, new uint[] { 1 })]
    [InlineData(1, new uint[] { 2 })]
    [InlineData(2, new uint[] { 3 })]
    public void AltitudeComparesRawHeightsWithoutRoundingNearbyItems(int comparison, uint[] expected)
    {
        var world = new WiredSelectorWorld(2, 2,
        [new(1, 1, "below", "", 0, 0, 0.996, 1, [(0, 0)]),
         new(2, 1, "equal", "", 0, 0, 1, 1, [(0, 0)]),
         new(3, 1, "above", "", 0, 0, 1.004, 1, [(0, 0)])], []);
        var selected = WiredSelectorModule.SelectRaw("wf_slc_furni_altitude", new() { IntParams = [comparison, 0, 0], Text = "1.00" },
            world, new(new(), new(), new()));
        Assert.Equal(expected, selected.Selection.FurniIds);
    }

    [Theory]
    [InlineData(0, new uint[] { 2 })]
    [InlineData(1, new uint[] { 3 })]
    [InlineData(2, new uint[] { 4 })]
    [InlineData(3, new uint[] { 1, 2, 3, 4 })]
    public void FurnitureOnFurnitureUsesAnchorOriginAndRelativeBaseHeight(int mode, uint[] expected)
    {
        var world = new WiredSelectorWorld(4, 4,
        [new(1, 1, "anchor", "", 1, 1, 1, 5, [(1, 1), (2, 1)]),
         new(2, 1, "above", "", 1, 1, 2, 1, [(1, 1)]),
         new(3, 1, "below", "", 1, 1, 0, 5, [(1, 1)]),
         new(4, 1, "same", "", 1, 1, 1, 1, [(1, 1)]),
         new(5, 1, "overlaps other tile", "", 2, 1, 2, 1, [(2, 1)])], []);
        var selected = WiredSelectorModule.SelectRaw("wf_slc_furni_onfurni", new() { IntParams = [mode, 100, 0, 0], SelectedItems = [1] },
            world, new(new(), new(), new()));
        Assert.Equal(expected, selected.Selection.FurniIds);
    }
    [Theory]
    [InlineData(0, new uint[] { 1, 2, 3 })]
    [InlineData(1, new uint[] { 2 })]
    public void RemoteExtendedShapeCombinesReferencedStackSelections(int type, uint[] expected)
    {
        var world = RemoteWorld();
        var selected = WiredSelectorModule.SelectRaw("wf_slc_remote",
            new() { IntParams = [0, 0, type, 0], SelectedItems = [10, 20] }, world, new(new(), new(), new()));
        Assert.Equal(expected, selected.Selection.FurniIds.Order());
    }

    [Fact]
    public void RemotePickingOneSelectorRunsOtherSelectorsOnThatStackIncludingNestedInversion()
    {
        var world = RemoteWorld() with
        {
            Furni = [.. RemoteWorld().Furni, new(11, 1, "nested remote", "", 5, 5, 1, 1, [(5, 5)], IsWired: true)],
            RemoteSelectors = new Dictionary<uint, WiredRemoteSelector>
            {
                [10] = new("wf_slc_furni_picks", new() { SelectedItems = [1, 2] }),
                [11] = new("wf_slc_remote", new() { IntParams = [0, 0, 0, 0], SelectedItems = [20] }),
                [20] = new("wf_slc_furni_picks", new() { IntParams = [0, 1], SelectedItems = [1, 2] })
            }
        };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_remote",
            new() { IntParams = [0, 0, 0, 0], SelectedItems = [10] }, world, new(new(), new(), new()));
        Assert.Equal(new uint[] { 1, 2, 3 }, selected.Selection.FurniIds.Order());
    }

    [Fact]
    public void RemoteComposesFilterInsideReferencedStackAndAcceptsOrdinaryFurnitureLocator()
    {
        var original = RemoteWorld();
        var world = original with
        {
            Furni = [.. original.Furni,
                new(11, 1, "filtering remote", "", 5, 5, 1, 1, [(5, 5)], IsWired: true),
                new(12, 1, "ordinary locator", "", 5, 5, 2, 1, [(5, 5)])],
            RemoteSelectors = new Dictionary<uint, WiredRemoteSelector>(original.RemoteSelectors!)
            {
                [11] = new("wf_slc_remote", new() { IntParams = [1, 0, 0, 0], SelectedItems = [20] })
            }
        };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_remote",
            new() { IntParams = [0, 0, 0, 0], SelectedItems = [12] }, world, new(new(), new(), new()));
        Assert.Equal(new uint[] { 2 }, selected.Selection.FurniIds);
    }

    [Fact]
    public void RemoteRandomCountSamplesDistinctStacksAndRunsAllSelectorsOnChosenTile()
    {
        var original = RemoteWorld();
        var world = original with
        {
            Furni = [.. original.Furni,
                new(11, 1, "additional selector", "", 5, 5, 1, 1, [(5, 5)], IsWired: true)],
            RemoteSelectors = new Dictionary<uint, WiredRemoteSelector>(original.RemoteSelectors!)
            {
                [11] = new("wf_slc_furni_picks", new() { SelectedItems = [3] })
            }
        };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_remote",
            new() { IntParams = [0, 0, 0, 1], SelectedItems = [10, 11, 20] }, world,
            new(new(), new(), new()), new KeepRemoteOrderRandom());
        Assert.Equal(new uint[] { 1, 2, 3 }, selected.Selection.FurniIds.Order());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(201)]
    public void RemoteSourceResolvesTilesInsteadOfUsingUnusedSavedPicks(int source)
    {
        var input = new WiredSelectorInputs(new(), new(), new());
        input.Triggering.FurniIds.Add(20);
        input.SelectorPool.FurniIds.Add(20);
        input.Signal.FurniIds.Add(20);
        var selected = WiredSelectorModule.SelectRaw("wf_slc_remote",
            new() { IntParams = [0, 0, 0, 0, source], SelectedItems = source == 100 ? [20] : [10] },
            RemoteWorld(), input);
        Assert.Equal(new uint[] { 2, 3 }, selected.Selection.FurniIds.Order());
    }

    [Theory]
    [InlineData(200)]
    [InlineData(900)]
    public void RemoteRejectsSourcesOutsideItsOfficialNativeAllowedList(int source)
    {
        Assert.Throws<ArgumentException>(() => WiredSelectorConfiguration.Normalize("wf_slc_remote",
            new() { IntParams = [0, 0, 0, 0, source] }));
    }

    [Fact]
    public void RemoteStackCycleCannotReenterThroughAnOrdinaryLocatorAlias()
    {
        var original = RemoteWorld();
        var world = original with
        {
            Furni = [.. original.Furni, new(11, 1, "cyclic remote", "", 5, 5, 1, 1, [(5, 5)], IsWired: true),
                new(12, 1, "ordinary alias", "", 5, 5, 2, 1, [(5, 5)])],
            RemoteSelectors = new Dictionary<uint, WiredRemoteSelector>(original.RemoteSelectors!)
            {
                [11] = new("wf_slc_remote", new() { IntParams = [0, 0, 0, 0], SelectedItems = [12] })
            }
        };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_remote",
            new() { IntParams = [0, 0, 0, 0], SelectedItems = [12] }, world, new(new(), new(), new()));
        Assert.Equal(new uint[] { 1, 2 }, selected.Selection.FurniIds.Order());
    }

    [Fact]
    public void RemoteVariableQueriesFollowTheAddressedStackEvenWhenThePickedBoxIsNotASelector()
    {
        var original = RemoteWorld();
        var world = original with
        {
            Furni = [.. original.Furni, new(11, 1, "variable selector", "", 5, 5, 1, 1, [(5, 5)], IsWired: true),
                new(12, 1, "ordinary alias", "", 5, 5, 2, 1, [(5, 5)])],
            RemoteSelectors = new Dictionary<uint, WiredRemoteSelector>(original.RemoteSelectors!)
            {
                [11] = new("wf_slc_furni_with_var", new())
            }
        };
        Assert.True(WiredSelectorVariableBridge.RequiresQueries("wf_slc_remote", new() { IntParams = [0, 0, 0, 0], SelectedItems = [12] }, world));
    }

    private sealed class KeepRemoteOrderRandom : Random
    {
        public override int Next(int maxValue) => maxValue - 1;
    }

    private static WiredSelectorWorld RemoteWorld() => new(10, 10,
    [new(1, 1, "one", "", 0, 0, 0, 1, [(0, 0)]),
     new(2, 1, "two", "", 0, 1, 0, 1, [(0, 1)]),
     new(3, 1, "three", "", 0, 2, 0, 1, [(0, 2)]),
     new(10, 1, "selector one", "", 5, 5, 0, 1, [(5, 5)], IsWired: true),
     new(20, 1, "selector two", "", 6, 6, 0, 1, [(6, 6)], IsWired: true)], [],
     RemoteSelectors: new Dictionary<uint, WiredRemoteSelector>
     {
         [10] = new("wf_slc_furni_picks", new() { SelectedItems = [1, 2] }),
         [20] = new("wf_slc_furni_picks", new() { SelectedItems = [2, 3] })
     });

}
