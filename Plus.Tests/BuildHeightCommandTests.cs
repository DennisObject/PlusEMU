using System.Collections.Concurrent;
using System.Reflection;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Commands.User;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task BuildHeightPlacesAndMovesFurnitureAtTheChosenHeightUntilReset()
    {
        var user = EnterAsOwner();
        var commands = new CommandManager([new BuildHeightCommand()], TestGameClientManager.Empty, _database, _interactionClock);
        var blocker = Furni(40, InteractionType.None, WiredBoxType.None);
        blocker.Definition.Stackable = false;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, blocker, 1, 1, 0, true, false, false));

        _client.GetHabbo().Access = UserAccess.Empty;
        Assert.False(await commands.Parse(_client, ":bh 25"));
        Assert.Null(user.BuildHeight);

        _client.GetHabbo().Access = UserAccess.Create([], [new(PermissionKeys.CommandBh, false)]);
        Assert.True(await commands.Parse(_client, ":bh 41"));
        Assert.Null(user.BuildHeight);
        Assert.True(await commands.Parse(_client, ":bh 2,5"));
        Assert.Equal(2.5, user.BuildHeight);

        // The blocker cannot be stacked on, but a build height places on top of it anyway.
        Inventory(new InventoryItem { Id = 41, Definition = Furni(41, InteractionType.None, WiredBoxType.None).Definition });
        await PlaceObject().Parse(_room, _client, ClientPacket("41 1 1 0"));
        Assert.Equal(2.5, _room.GetRoomItemHandler().GetItem(41)!.GetZ);

        var moved = Furni(42, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, moved, 3, 0, 0, true, false, false));
        Assert.True(await commands.Parse(_client, ":bh 25"));
        PlacementService(() => { }).Move(_room, _client, 42, 3, 1, 0);
        Assert.Equal((3, 1, 25.0), (moved.GetX, moved.GetY, moved.GetZ));

        Assert.True(await commands.Parse(_client, ":bh"));
        Assert.Null(user.BuildHeight);
        Inventory(new InventoryItem { Id = 43, Definition = Furni(43, InteractionType.None, WiredBoxType.None).Definition });
        await PlaceObject().Parse(_room, _client, ClientPacket("43 1 1 0"));
        Assert.Null(_room.GetRoomItemHandler().GetItem(43));
    }

    private RoomUser EnterAsOwner()
    {
        var user = new RoomUser(7, RoomId, 1, _room, _client, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 3, Y = 3 };
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_room.GetRoomUserManager())!;
        users[user.VirtualId] = user;

        return user;
    }
}
