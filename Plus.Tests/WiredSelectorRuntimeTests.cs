using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Xunit;
using static Plus.Tests.WiredSelectorTests;

namespace Plus.Tests;

public sealed class WiredSelectorRuntimeTests
{
    [Fact]
    public void AddonFactoriesUseInteractionNameAndDoNotDeferAddonsUntilAfterConditions()
    {
        foreach (var name in WiredAddonModule.Names) {
            var item = new Item { Id = 9, Definition = new() { ItemName = "hotel_specific_name", InteractionName = name } };
            var addon = WiredAddonFactory.Create(null!, item, new(), TestGroupManager.Empty);
            Assert.NotNull(addon);
            Assert.Equal(name, addon.Descriptor.CanonicalName);
            Assert.False(addon.AfterConditions);
        }

        Assert.Null(WiredAddonFactory.Create(null!, new() { Definition = new() { ItemName = "wf_xtra_var_fx_health" } }, new(), TestGroupManager.Empty));
    }

    [Fact]
    public void AllSelectorFactoriesResolveTheActiveDescriptorAndVariableBoxesRequireProvider()
    {
        foreach (var name in WiredSelectorModule.Names) {
            var item = new Item { Id = 9, Definition = new() { ItemName = name.ToUpperInvariant() } };
            var box = WiredSelectorFactory.Create(null!, item, new(), TestGroupManager.Empty,
                _ => throw new InvalidOperationException("Not executed by this construction test"));
            Assert.NotNull(box);
            Assert.Equal(name, box.Descriptor.CanonicalName);
            Assert.Equal(WiredBoxSupport.Implemented, box.Descriptor.Support);

            if (name.EndsWith("_with_var", StringComparison.Ordinal)) {
                Assert.Null(WiredSelectorFactory.Create(null!, item, new(), TestGroupManager.Empty));
            }
        }

        Assert.Null(WiredSelectorFactory.Create(null!, new() { Definition = new() { ItemName = "not_wired" } }, new(), TestGroupManager.Empty));
    }

    [Fact]
    public void FailedValidationDoesNotChangeLiveBoxAndPublishKeepsExactActiveEnvelopeFields()
    {
        // Native pick validation reads the live room, so the box lives in a room whose handler holds the picked item.
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_roomItemHandling", Private)!.SetValue(room, handler);
        var picked = new Item { Id = 42, Definition = new() { Type = ItemType.Floor } };
        ((ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", Private)!.GetValue(handler)!)[picked.Id] = picked;
        var item = new Item { Id = 9, Definition = new() { ItemName = "wf_slc_furni_neighborhood" } };
        typeof(Item).GetField("_room", Private)!.SetValue(item, room);
        var box = WiredSelectorFactory.Create(room, item, new(), TestGroupManager.Empty)!;
        var original = box.Configuration;
        Assert.False(box.TryValidateConfiguration(Config([4, 0, 0, 0, 0, 2, 1, 1]), out _, out var error));
        Assert.NotEmpty(error);
        Assert.Same(original, box.Configuration);
        var native = WiredNativeEditorProjection.DefaultNative(box.Descriptor) with
        {
            OwnedIntParams = [0, 1, 1, 1, .. new int[13]],
            FurniSourceTypes = [100],
            PrimaryItems = [new(42, false)]
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(item.Id, box.Descriptor, native, out var compiled));
        Assert.True(box.TryValidateConfiguration(compiled, out var valid, out _));
        box.ApplyConfiguration(valid);
        Assert.Same(valid, box.Configuration);
        Assert.Equal("42", box.ItemsData);
        Assert.Equal(native.OwnedIntParams.ToArray(), box.Configuration.IntParams.ToArray());
        Assert.False(box.Execute());
    }

    [Fact]
    public void RecentActionStateDoesNotTransferToAReusedVirtualIdAndExpiresAfterFiveSeconds()
    {
        var original = new RoomUser(500, 1, 4, null!, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var reused = new RoomUser(600, 1, 4, null!, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var state = new WiredSelectorRoomState();
        state.Observe(new(WiredEventKind.AvatarAction) { Actor = original, Action = 9, Code = 3 }, 1000);
        Assert.Equal((9, 3, 1000L), state.Read(original));
        Assert.Null(state.Read(reused));
        state.Observe(new(WiredEventKind.Periodic), 6000);
        Assert.NotNull(state.Read(original));
        state.Observe(new(WiredEventKind.Periodic), 6001);
        Assert.Null(state.Read(original));
        state.Observe(new(WiredEventKind.AvatarAction) { Actor = reused, Action = 1 }, 7000);
        state.Observe(new(WiredEventKind.Leave) { Actor = reused }, 7001);
        Assert.Null(state.Read(reused));
    }
}
