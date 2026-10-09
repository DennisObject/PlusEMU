using System.Reflection;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void WallIdentitiesDoNotExpandDirectOrNestedFloorSelectorInversion(int remoteDepth)
    {
        WallSnapshotInstallStore();
        var picked = Furni(301, InteractionType.None, WiredBoxType.None);
        var other = Furni(302, InteractionType.None, WiredBoxType.None);
        var wall = WallSnapshotItem(303, ":w=1,2 l=11,53 l");
        var items = new List<Item> { picked, other, wall };
        var boxes = new List<IWiredConfiguredItem>();
        var wired = _room.GetWired();

        IWiredConfiguredItem Box(uint id, string name, WiredConfiguration configuration, int tile)
        {
            Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
            var item = Furni(id, descriptor.Category == WiredBoxCategory.Trigger ? InteractionType.WiredTrigger : InteractionType.WiredEffect, WiredBoxType.None);
            item.GetX = tile;
            item.GetY = tile;
            items.Add(item);
            var box = wired.CreateConfiguredBox(item, descriptor)!;
            Assert.True(box.TryValidateConfiguration(configuration, out var validated, out var error), error);
            box.ApplyConfiguration(validated);
            boxes.Add(box);

            return box;
        }

        Box(400, "wf_trg_game_starts", new(), 0);
        var selector = Box(401, "wf_slc_furni_picks", new() { IntParams = [0, 1], SelectedItems = [picked.Id] }, remoteDepth);

        for (var depth = remoteDepth - 1; depth >= 0; depth--) {
            selector = Box((uint)(402 + depth), "wf_slc_remote",
                new() { IntParams = [0, 0, 0, 0, 100], SelectedItems = [selector.Item.Id] }, depth);
        }

        var actionItem = Furni(410, InteractionType.WiredEffect, WiredBoxType.None);
        items.Add(actionItem);
        var capture = new WallSelectorCapture(_room, actionItem);

        foreach (var item in items) {
            item.RoomId = RoomId;
        }

        _room.GetRoomItemHandler().LoadFurniture(items);

        foreach (var box in boxes) {
            Assert.True(wired.AddBox(box));
        }

        Assert.True(wired.AddBox(capture));
        var engine = (WiredStackEngine)wired.GetType().GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
        Assert.True(engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.GameStart)));
        engine.OnFastCycle();
        var context = Assert.Single(capture.Contexts);
        var floorIds = _room.GetRoomItemHandler().GetFloor.Select(item => item.Id).ToHashSet();
        Assert.All(context.Selected.FurniIds, id => Assert.Contains(id, floorIds));
        Assert.Equal(new[] { other.Id }, context.Selected.FurniIds.Where(id => id is 301 or 302));
        Assert.DoesNotContain(wall.Id, context.Selected.FurniIds);
        Assert.Same(wall, Assert.Single(context.Targets.ResolveFurni(context, [wall.Id], WiredSources.Selected)));
    }

    private sealed class WallSelectorCapture(Room room, Item item) : WiredModernBox(room, item,
        WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_toggle_state")), IWiredContextualAction
    {
        public List<WiredRuntimeContext> Contexts { get; } = [];
        public bool IsNegative => false;
        public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        {
            validated = proposed;
            error = string.Empty;

            return true;
        }

        public override bool Execute(WiredRuntimeContext context)
        {
            Contexts.Add(context);

            return true;
        }
    }
}
