using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredSelectorReviewRegressionTests
{
    [Theory]
    [InlineData("wf_slc_furni_onfurni")]
    [InlineData("wf_slc_users_onfurni")]
    public void ExplicitTriggerSourceWinsOverRetainedSavedPicks(string name)
    {
        var f = new Fixture();
        var selector = f.Selector(name, new()
        {
            SelectedItems = [2],
            IntParams = name == "wf_slc_furni_onfurni" ? [0, 0, 0, 0] : [0, 0, 0]
        });
        var result = selector.Select(f.Context);

        if (name == "wf_slc_furni_onfurni") {
            Assert.Equal(new uint[] { 3 }, result.Selection.FurniIds);
        }
        else {
            Assert.Equal(new[] { 11 }, result.Selection.UserIds);
        }

        Assert.Equal(1, f.WorldCaptures);
    }

    [Theory]
    [InlineData("wf_slc_furni_onfurni")]
    [InlineData("wf_slc_users_onfurni")]
    public void MissingSourceFieldUsesLegacyPickedAnchorFallback(string name)
    {
        var f = new Fixture();
        var selector = f.Selector(name, new()
        {
            SelectedItems = [2],
            IntParams = name == "wf_slc_furni_onfurni" ? [0] : []
        });
        var result = selector.Select(f.Context);

        if (name == "wf_slc_furni_onfurni") {
            Assert.Equal(new uint[] { 4 }, result.Selection.FurniIds);
        }
        else {
            Assert.Equal(new[] { 12 }, result.Selection.UserIds);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllOnTileIncludesItsAnchorEvenWhenItIsTheOnlyFurniture(bool anchorOnly)
    {
        var f = new Fixture();

        if (anchorOnly) {
            f.World = f.World with { Furni = f.World.Furni.Where(x => x.Id != 4).ToArray() };
        }

        var selector = f.Selector("wf_slc_furni_onfurni", new() { SelectedItems = [2], IntParams = [3, 100, 0, 0] });
        Assert.Equal(anchorOnly ? new uint[] { 2 } : [2, 4], selector.Select(f.Context).Selection.FurniIds.Order());
    }

    [Theory]
    [InlineData(1, "Above saved")]
    [InlineData(2, "Above saved;Above trigger")]
    public void NativeCaptureFormatterPreservesSavedPickOrderAndSingleFirst(int mode, string expected)
    {
        var f = new Fixture();
        var addon = f.Addon(new() { IntParams = [mode, 100], SelectedItems = [4, 3], Text = "items\t;" });
        Assert.True(addon.Apply(f.Context));
        Assert.Equal(expected, f.Context.Policy.FormatText(f.Context, "$(items)"));
        Assert.Equal(1, f.WorldCaptures);
    }

    [Fact]
    public void SavedOrderReorderingRetainsSourceCapsAndObjectIdentityGuards()
    {
        var f = new Fixture();
        f.Context.Policy.Addons.FurniLimit = 1;
        Assert.True(f.Addon(new() { IntParams = [2, 100], SelectedItems = [4, 3], Text = "items\t;" }).Apply(f.Context));
        var capped = Assert.Single(f.Context.Targets.ResolveFurni(f.Context, [4, 3], WiredSources.Selected));
        Assert.Equal(capped.Definition.PublicName, f.Context.Policy.FormatText(f.Context, "$(items)"));
        f.Items.Remove(capped);
        f.Items.Add(new() { Id = capped.Id, Definition = new() { PublicName = "Replacement" } });
        Assert.Equal("", f.Context.Policy.FormatText(f.Context, "$(items)"));
        Assert.Equal(1, f.WorldCaptures);
    }

    private sealed class Fixture
    {
        private readonly Room _room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        private readonly WiredSelectorRoomState _state = new();
        public readonly List<Item> Items = [Item(1, "Trigger"), Item(2, "Saved"), Item(3, "Above trigger"), Item(4, "Above saved")];
        public WiredSelectorWorld World = new(20, 20,
        [
            new(1, 1, "Trigger", "", 1, 1, 0, 1, [(1, 1)]),
            new(2, 1, "Saved", "", 8, 8, 0, 1, [(8, 8)]),
            new(3, 1, "Above trigger", "", 1, 1, 2, 1, [(1, 1)]),
            new(4, 1, "Above saved", "", 8, 8, 2, 1, [(8, 8)])
        ], [new(11, "On trigger", WiredSelectorEntityKind.Player, 1, 1), new(12, "On saved", WiredSelectorEntityKind.Player, 8, 8)]);
        public readonly WiredRuntimeContext Context;
        public int WorldCaptures;
        public Fixture()
        {
            RoomUser[] users = [new(111, 1, 11, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused), new(112, 1, 12, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused)];
            Context = new(_room, new(WiredEventKind.Enter) { EventItem = Items[0] },
                new(() => Items, () => users, id => Items.FirstOrDefault(x => x.Id == id),
                    id => users.FirstOrDefault(x => x.VirtualId == id)), new Operations());
            Context.Triggering.FurniIds.Add(1);
        }
        private static Item Item(uint id, string name) => new() { Id = id, Definition = new() { PublicName = name } };
        private WiredSelectorWorld ReadWorld(WiredRuntimeContext _)
        {
            WorldCaptures++;

            return World;
        }
        public IWiredContextualSelector Selector(string name, WiredConfiguration configuration)
        {
            var selector = WiredSelectorFactory.Create(_room, new() { Id = 100, Definition = new() { InteractionName = name } },
                _state, TestGroupManager.Empty, readWorld: ReadWorld)!;
            Assert.True(selector.TryValidateConfiguration(configuration, out var valid, out var error), error);
            selector.ApplyConfiguration(valid);

            return selector;
        }
        public IWiredContextualAddon Addon(WiredConfiguration configuration)
        {
            var addon = WiredAddonFactory.Create(_room, new() { Id = 200, Definition = new() { InteractionName = "wf_xtra_text_output_furni_name" } },
                _state, TestGroupManager.Empty, readWorld: ReadWorld)!;
            Assert.True(addon.TryValidateConfiguration(configuration, out var valid, out var error), error);
            addon.ApplyConfiguration(valid);

            return addon;
        }
    }
    private sealed class Operations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => false;
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> targets, WiredSelection selection, bool negative = false) => false;
        public void ResetTimers(IEnumerable<Item> targets) { }
    }
}
