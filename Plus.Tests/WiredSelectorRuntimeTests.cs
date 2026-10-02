using Plus.HabboHotel.Items;
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
    public void AddonFactoriesUseInteractionNameAndSeparatePostConditionGatesFromPolicies()
    {
        foreach (var name in WiredAddonModule.Names)
        {
            var item = new Item { Id = 9, Definition = new() { ItemName = "hotel_specific_name", InteractionName = name } };
            var addon = WiredAddonFactory.Create(null!, item, new());
            Assert.NotNull(addon);
            Assert.Equal(name, addon.Descriptor.CanonicalName);
            Assert.Equal(name == "wf_xtra_execution_limit", addon.AfterConditions);
        }
        Assert.Null(WiredAddonFactory.Create(null!, new() { Definition = new() { ItemName = "wf_xtra_var_fx_health" } }, new()));
    }

    [Fact]
    public void AllSelectorFactoriesResolveTheActiveDescriptorAndVariableBoxesRequireProvider()
    {
        foreach (var name in WiredSelectorModule.Names)
        {
            var item = new Item { Id = 9, Definition = new() { ItemName = name.ToUpperInvariant() } };
            var box = WiredSelectorFactory.Create(null!, item, new(), _ => throw new InvalidOperationException("Not executed by this construction test"));
            Assert.NotNull(box);
            Assert.Equal(name, box.Descriptor.CanonicalName);
            Assert.Equal(WiredBoxSupport.Implemented, box.Descriptor.Support);
            if (name.EndsWith("_with_var", StringComparison.Ordinal))
                Assert.Null(WiredSelectorFactory.Create(null!, item, new()));
        }
        Assert.Null(WiredSelectorFactory.Create(null!, new() { Definition = new() { ItemName = "not_wired" } }, new()));
    }

    [Fact]
    public void FailedValidationDoesNotChangeLiveBoxAndPublishKeepsExactActiveEnvelopeFields()
    {
        var box = WiredSelectorFactory.Create(null!, new() { Definition = new() { ItemName = "wf_slc_furni_neighborhood" } }, new())!;
        var original = box.Configuration;
        Assert.False(box.TryValidateConfiguration(Config([4, 0, 0, 0, 0, 2, 1, 1]), out _, out var error));
        Assert.NotEmpty(error);
        Assert.Same(original, box.Configuration);
        Assert.True(box.TryValidateConfiguration(Config([4, 1, 1, 0, 0, 1, 1, 1], [42]), out var valid, out _));
        box.ApplyConfiguration(valid);
        Assert.Same(valid, box.Configuration);
        Assert.Equal("42", box.ItemsData);
        Assert.Equal(new[] { 4, 1, 1, 0, 0, 1, 1, 1 }, box.Configuration.IntParams);
        Assert.False(box.Execute());
    }

    [Fact]
    public void RecentActionStateDoesNotTransferToAReusedVirtualIdAndExpiresAfterFiveSeconds()
    {
        var original = new RoomUser(500, 1, 4, null!);
        var reused = new RoomUser(600, 1, 4, null!);
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
