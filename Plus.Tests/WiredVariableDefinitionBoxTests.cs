using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableDefinitionBoxTests
{
    [Theory]
    [InlineData("wf_var_user", "score", 1, 10)]
    [InlineData("wf_var_furni", "score", 1, 1)]
    [InlineData("wf_var_room", "score", 10, 7)]
    [InlineData("wf_var_context", "score", 1, -1)]
    [InlineData("wf_var_echo", "{\"variableName\":\"alias\",\"sourceTargetType\":0,\"sourceVariableToken\":\"custom:20\"}", -1, -1)]
    [InlineData("wf_var_reference", "{\"variableName\":\"alias\",\"sourceTargetType\":0,\"sourceRoomId\":2,\"sourceVariableItemId\":20,\"readOnly\":true}", -1, -1)]
    public void PassiveDefinitionValidatesWithoutPublishingOrExecutingAnAction(string name, string text, int first, int second)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); room.Id = 1; room.OwnerId = 5;
        var item = new Item { Id = 10, Definition = new() { ItemName = "arbitrary_catalog_name", InteractionName = name } };
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        var box = new WiredVariableDefinitionBox(room, item, descriptor);
        var proposed = new WiredConfiguration { Text = text, IntParams = first < 0 ? [] : second < 0 ? [first] : [first, second] };
        Assert.True(box.TryValidateConfiguration(proposed, out var valid, out var error), error);
        Assert.Empty(box.Configuration.Text); // Pure validation has no publication or persistence side effects.
        box.ApplyConfiguration(valid);
        Assert.Equal(proposed, box.Configuration); Assert.False(box.Execute());
        Assert.False(box.TryValidateConfiguration(proposed with { Text = "invalid name with spaces" }, out _, out _));
        Assert.Equal(proposed, box.Configuration);
    }
}
