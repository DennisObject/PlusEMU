using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class WiredSignalValidationTests
{
    [Theory]
    [InlineData("antenna", ItemType.Floor, true)]
    [InlineData("wf_xtra_exec_in_order", ItemType.Floor, false)]
    [InlineData("antenna", ItemType.Wall, false)]
    public void ReceiveSignalSaveRequiresPickedAntennas(string interaction, ItemType type, bool accepted)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors,
            TestItemRuntime.Travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        var picked = new Item { Id = 1, Definition = new() { Type = type, InteractionName = interaction } };
        var field = type == ItemType.Wall ? "_wallItems" : "_floorItems";
        var items = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
        items[picked.Id] = picked;
        Assert.True(WiredBoxRegistry.TryGet("wf_trg_recv_signal", out var descriptor));
        var trigger = new WiredModernTrigger(room, new Item { Id = 2 }, descriptor);
        var saved = WiredConfigurationSave.TrySave(trigger, new() { IntParams = [0, 100], SelectedItems = [1] },
            TestWiredConfigurationStore.Instance, out var error, existsInRoom: id => handler.GetItem(id) != null);
        Assert.Equal(accepted, saved);

        if (!accepted) {
            Assert.Equal("wiredfurni.error.require_antenna_furni", error);
            Assert.Empty(trigger.Configuration.SelectedItems);
        }
    }
}
