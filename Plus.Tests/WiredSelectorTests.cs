using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Xunit;

namespace Plus.Tests;

public sealed class WiredSelectorTests
{
    internal static WiredConfiguration Config(int[]? fields = null, uint[]? picks = null, string text = "") =>
        new() { IntParams = (fields ?? []).ToImmutableArray(), SelectedItems = (picks ?? []).ToImmutableArray(), Text = text };
    // The native Neighborhood form: anchor kind, centre offset, then the 441-tile spiral bitmap (tile 0 is the anchor).
    internal static WiredConfiguration Hood(int anchorKind, int offsetX, int offsetY, int anchor, params int[] tiles)
    {
        var words = new int[14];

        foreach (var tile in tiles) {
            words[tile / 32] |= unchecked((int)(1u << (tile % 32)));
        }

        return new()
        {
            IntParams = [anchorKind, offsetX, offsetY, .. words],
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("anchor", anchor),
            UserSources = ImmutableDictionary<string, int>.Empty.Add("anchor", anchor)
        };
    }

    internal static WiredSelectorWorld World() => new(10, 10,
    [
        new(1, 100, "Chair", "0", 1, 1, 0, 1, [(1, 1), (2, 1)]),
        new(2, 100, "High chair", "1", 2, 1, 1, 1, [(2, 1)]),
        new(3, 200, "Lamp", "0", 4, 4, 0, 1, [(4, 4)]),
        new(4, 100, "Wall", "0", 1, 1, 0, 1, [(1, 1)], false),
        new(5, 100, "Wired", "0", 1, 2, 0, 1, [(1, 2)], IsWired: true)
    ],
    [
        new(1, "Ada", WiredSelectorEntityKind.Player, 2, 1, 1, new HashSet<int> { 9 }, 7, Sitting: true, Sign: 3, Dance: 2, EquippedGroupId: 9),
        new(2, "Bob", WiredSelectorEntityKind.Bot, 4, 4),
        new(3, "Cat", WiredSelectorEntityKind.Pet, 0, 0),
        new(4, "Ana", WiredSelectorEntityKind.Player, 1, 2, 2, new HashSet<int> { 10 }, EquippedGroupId: 10)
    ], 9, new Dictionary<uint, WiredRemoteSelector> { [99] = new("wf_slc_furni_picks", Config(picks: [3])) });

    internal static WiredSelectorInputs Inputs()
    {
        var triggering = new WiredSelectedIds();
        triggering.FurniIds.Add(1);
        triggering.UserIds.Add(1);
        var signal = new WiredSelectedIds();
        signal.FurniIds.Add(3);
        signal.UserIds.Add(2);

        return new(triggering, new(), signal, ClickedUserId: 4,
            FurniVariablePredicate: (_, _, id) => id == 3,
            UserVariablePredicate: (_, _, id) => id == 4);
    }

    public static IEnumerable<object[]> AllSelectors()
    {
        yield return ["wf_slc_furni_bytype", Config([0, 0], [1]), new uint[] { 1, 2 }, Array.Empty<int>()];
        yield return ["wf_slc_furni_picks", Config(picks: [3]), new uint[] { 3 }, Array.Empty<int>()];
        yield return ["wf_slc_users_bytype", Config([2]), Array.Empty<uint>(), new[] { 3 }];
        yield return ["wf_slc_users_team", Config([1]), Array.Empty<uint>(), new[] { 1 }];
        yield return ["wf_slc_furni_onfurni", Config([0, 100], [1]), Array.Empty<uint>(), Array.Empty<int>()];
        yield return ["wf_slc_furni_signal", Config(), new uint[] { 3 }, Array.Empty<int>()];
        yield return ["wf_slc_furni_neighborhood", Hood(0, 0, 0, WiredSources.Trigger, 0, 1), new uint[] { 1, 2 }, Array.Empty<int>()];
        yield return ["wf_slc_furni_area", Config([1, 1, 2, 1]), new uint[] { 1, 2 }, Array.Empty<int>()];
        yield return ["wf_slc_users_onfurni", Config([100], [1]), Array.Empty<uint>(), new[] { 1 }];
        yield return ["wf_slc_users_byaction", Config([6]), Array.Empty<uint>(), new[] { 1 }];
        yield return ["wf_slc_users_signal", Config(), Array.Empty<uint>(), new[] { 2 }];
        yield return ["wf_slc_users_byname", Config(text: " ADA \nana"), Array.Empty<uint>(), new[] { 1, 4 }];
        yield return ["wf_slc_users_neighborhood", Hood(1, 1, -1, WiredSources.Trigger, 0), Array.Empty<uint>(), new[] { 4 }];
        yield return ["wf_slc_users_area", Config([1, 1, 2, 1]), Array.Empty<uint>(), new[] { 1 }];
        yield return ["wf_slc_users_handitem", Config([0]), Array.Empty<uint>(), new[] { 1 }];
        yield return ["wf_slc_users_group", Config([0]), Array.Empty<uint>(), new[] { 1 }];
        yield return ["wf_slc_furni_altitude", Config([1], text: "1.004"), Array.Empty<uint>(), Array.Empty<int>()];
        yield return ["wf_slc_furni_with_var", Config(text: "custom:77"), new uint[] { 3 }, Array.Empty<int>()];
        yield return ["wf_slc_users_with_var", Config(text: "custom:77"), Array.Empty<uint>(), new[] { 4 }];
        yield return ["wf_slc_remote", Config(picks: [99]), new uint[] { 3 }, Array.Empty<int>()];
    }

    [Theory]
    [MemberData(nameof(AllSelectors))]
    public void ActiveEditorConfigurationSelectsExpectedLiveTargets(string name, WiredConfiguration c, uint[] furni, int[] users)
    {
        var result = WiredSelectorModule.SelectRaw(name, c, World(), Inputs());
        Assert.Equal(furni.Order(), result.Selection.FurniIds.Order());
        Assert.Equal(users.Order(), result.Selection.UserIds.Order());
    }

    [Fact]
    public void FilteringAnAlreadyEmptyKindDoesNotRestoreTriggerTargetsOrEraseOtherKind()
    {
        var input = Inputs() with { FurniModified = true };
        input.SelectorPool.UserIds.Add(4);
        var raw = WiredSelectorModule.SelectRaw("wf_slc_furni_picks", Config([1, 0], [1]), World(), input);
        var composed = WiredSelectorModule.Compose(raw, World(), input);
        Assert.Empty(composed.FurniIds);
        Assert.Equal(new[] { 4 }, composed.UserIds);
        Assert.Equal(new[] { 4 }, input.SelectorPool.UserIds);
    }

    [Fact]
    public void InversionIsAppliedOnceAndOnlyToFloorFurniture()
    {
        var result = WiredSelectorModule.Compose(WiredSelectorModule.SelectRaw("wf_slc_furni_picks",
            Config([0, 1], [1]), World(), Inputs()), World(), Inputs());
        Assert.Equal(new uint[] { 2, 3 }, result.FurniIds.Order());
    }

    [Fact]
    public void RemoteSelectorsKeepEmptyFilterResultsAndTerminateCycles()
    {
        var remotes = new Dictionary<uint, WiredRemoteSelector>
        {
            [90] = new("wf_slc_furni_picks", Config(picks: [3])),
            [91] = new("wf_slc_furni_picks", Config([1, 0], [1])),
            [92] = new("wf_slc_furni_picks", Config([1, 0], [1])),
            [93] = new("wf_slc_remote", Config([0, 0, 0, 0, 100], [93]))
        };
        var world = World() with { RemoteSelectors = remotes };
        // Mode 1 intersects the referenced stacks, so an empty filtered stack keeps the whole result empty.
        var result = WiredSelectorModule.SelectRaw("wf_slc_remote", Config([0, 0, 1, 0, 100], [90, 91, 92, 93]), world, Inputs());
        Assert.Empty(result.Selection.FurniIds);
    }

    [Fact]
    public void TransientActionsExpireButCurrentSignAndDanceUseParameterFilters()
    {
        var world = World() with { Users = [World().Users[0] with { LastAction = 1, LastActionAtMs = 100 }] };
        Assert.Single(WiredSelectorModule.SelectRaw("wf_slc_users_byaction", Config([1]), world, Inputs() with { NowMs = 5100 }).Selection.UserIds);
        Assert.Empty(WiredSelectorModule.SelectRaw("wf_slc_users_byaction", Config([1]), world, Inputs() with { NowMs = 5101 }).Selection.UserIds);
        Assert.Empty(WiredSelectorModule.SelectRaw("wf_slc_users_byaction", Config([9, 1, 4]), world, Inputs()).Selection.UserIds);
        Assert.Single(WiredSelectorModule.SelectRaw("wf_slc_users_byaction", Config([10, 0, 0, 1, 2]), world, Inputs()).Selection.UserIds);
    }

    [Fact]
    public void EmptyNeighborhoodSelectsNothingAndTheFullEditorGridIsAccepted()
    {
        foreach (var (name, kind) in new[] { ("wf_slc_furni_neighborhood", 0), ("wf_slc_users_neighborhood", 1) }) {
            var empty = WiredSelectorConfiguration.Normalize(name, Hood(kind, 0, 0, WiredSources.Trigger));
            var result = WiredSelectorModule.SelectRaw(name, empty, World(), Inputs());
            Assert.Empty(result.Selection.FurniIds);
            Assert.Empty(result.Selection.UserIds);

            // All 441 tiles of the 21x21 editor grid.
            var full = WiredSelectorConfiguration.Normalize(name, Hood(kind, 0, 0, WiredSources.Trigger, Enumerable.Range(0, 441).ToArray()));
            Assert.True(WiredLegacyProtocol.IsWithinLimits(full));
            var selected = WiredSelectorModule.SelectRaw(name, full, World(), Inputs()).Selection;

            if (name == "wf_slc_furni_neighborhood") {
                Assert.Equal(new uint[] { 1, 2, 3 }, selected.FurniIds.Order());
            }
            else {
                Assert.Equal(new[] { 1, 2, 3, 4 }, selected.UserIds.Order());
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(7)]
    public void AwakeAndStandingSelectorsReadCurrentStateWithoutARecentAction(int action)
    {
        var users = World().Users;
        var world = World() with
        {
            Users = [
            users[0] with { Sitting = false },
            users[1] with { Sitting = true, Idle = true, LastAction = action, LastActionAtMs = 100 },
            users[2] with { Lying = true, Idle = true }
        ]
        };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_users_byaction", Config([action]), world,
            Inputs() with { NowMs = 200 }).Selection;
        Assert.Equal(new[] { 1 }, selected.UserIds);
    }

    [Fact]
    public void NeighborhoodUsesAnchorOffsetsAndRejectsAnythingButTheNativeBitmap()
    {
        // The trigger user stands on (2,1); centre offset (1,0) with the east tile selects the anchor's own tile.
        var raw = WiredSelectorModule.SelectRaw("wf_slc_users_neighborhood", Hood(1, 1, 0, WiredSources.Trigger, 1), World(), Inputs());
        Assert.Equal(new[] { 1 }, raw.Selection.UserIds);
        Assert.Throws<ArgumentException>(() => WiredSelectorModule.SelectRaw("wf_slc_users_neighborhood",
            Config([1, 0, 0, 1, 0]), World(), Inputs()));
    }

    [Fact]
    public void VariableSelectorsUseFinalTwoSwitchesAndRequireAnActualPredicate()
    {
        var result = WiredSelectorModule.SelectRaw("wf_slc_users_with_var", Config([1, 2, 0, 8, 0, 0, 0, 1, 1]), World(), Inputs());
        Assert.True(result.FiltersExisting);
        Assert.True(result.Invert);
        Assert.Throws<InvalidOperationException>(() => WiredSelectorModule.SelectRaw("wf_slc_users_with_var", Config(), World(), Inputs() with { UserVariablePredicate = null }));
    }
}
