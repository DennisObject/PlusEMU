using System.Data.Common;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Commands.User;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task HideWiredTogglesTheRoomsWiredFurnitureForOwnersOnly()
    {
        EnterAsOwner();
        var persisted = new List<object?>();
        var database = Proxy<IDatabase>((method, _) => method == "Connection"
            ? new NoOpConnection(write: (_, parameters) => persisted.Add(parameters.Cast<DbParameter>().Single(p => p.ParameterName.TrimStart('@') == "hidden").Value))
            : throw new InvalidOperationException(method));
        var commands = new CommandManager([new HideWiredCommand(database)], TestGameClientManager.Empty, _database, _interactionClock);
        var wired = Furni(50, InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOnFurni);
        var chair = Furni(51, InteractionType.None, WiredBoxType.None);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, wired, 1, 1, 0, true, false, false));
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, chair, 2, 1, 0, true, false, false));
        _room.GetWired().LoadWiredBox(wired);
        _client.GetHabbo().Access = UserAccess.Create([], [new(PermissionKeys.CommandHidewired, false)]);

        _client.GetHabbo().Username = "visitor";
        Assert.True(await commands.Parse(_client, ":hidewired"));
        Assert.False(_room.HideWired);

        _client.GetHabbo().Username = "owner";
        _client.Sent.Clear();
        Assert.True(await commands.Parse(_client, ":hidewired"));
        Assert.True(_room.HideWired);
        Assert.Contains(ServerPacketHeader.ObjectRemoveComposer, _client.Sent);
        Assert.Equal([chair], _room.VisibleFloorItems);

        // A hidden box does not flash when its stack fires.
        _room.GetWired().OnEvent(wired);
        Assert.NotEqual("1", wired.LegacyDataString);

        _client.Sent.Clear();
        Assert.True(await commands.Parse(_client, ":hidewired"));
        Assert.False(_room.HideWired);
        Assert.Contains(ServerPacketHeader.ObjectsComposer, _client.Sent);
        Assert.Equal([wired, chair], _room.VisibleFloorItems.OrderBy(item => item.Id));
        Assert.Equal([true, false], persisted);
    }
}
