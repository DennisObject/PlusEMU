using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void DiceServiceKeepsTheInteractorDistanceAndRightsPolicy()
    {
        var item = Furni(51, InteractionType.Dice, WiredBoxType.None);
        item.ExtraData = new LegacyDataFormat { Data = "0" };
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        var actor = Recipient();
        actor.X = 1;
        actor.Y = 0;
        _client.GetHabbo().Username = "visitor"; // The dice interactor permits adjacent visitors.
        var service = new FurnitureUseService(null!, null!);

        service.RollDice(_room, _client, new(item.Id, 0));
        Assert.Equal("-1", item.LegacyDataString);
        item.LegacyDataString = "4";
        service.TurnOffDice(_room, _client, item.Id);
        Assert.Equal("0", item.LegacyDataString);
    }

    [Fact]
    public void OneWayGateServiceUsesTheExistingGateInteraction()
    {
        var item = Furni(52, InteractionType.OneWayGate, WiredBoxType.None);
        item.ExtraData = new LegacyDataFormat { Data = "0" };
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        var actor = Recipient();
        actor.X = item.SquareInFront.X;
        actor.Y = item.SquareInFront.Y;

        new FurnitureUseService(null!, null!).UseOneWayGate(_room, _client, item.Id);

        Assert.True(actor.InteractingGate);
        Assert.Equal(item.Id, actor.GateId);
        Assert.Equal(actor.HabboId, item.InteractingUser);
    }

    [Fact]
    public void GateServiceDoesNotTriggerOtherFurniture()
    {
        var item = Furni(53, InteractionType.Scoreboard, WiredBoxType.None);
        item.ExtraData = new LegacyDataFormat { Data = "0" };
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        item.LegacyDataString = "9";
        _client.Packets.Clear();

        new FurnitureUseService(null!, null!).UseOneWayGate(_room, _client, item.Id);

        Assert.Equal("9", item.LegacyDataString);
        Assert.Empty(_client.Packets);
    }

    [Fact]
    public void WallServicePublishesTheInteractorResultBeforeQuestProgress()
    {
        var item = Furni(54, InteractionType.Scoreboard, WiredBoxType.None, ItemType.Wall);
        item.ExtraData = new LegacyDataFormat { Data = "0" };
        Assert.True(_room.GetRoomItemHandler().SetWallItem(_client, item));
        Recipient();
        item.LegacyDataString = "0";
        _client.Packets.Clear();
        var progressed = 0;
        var quests = Proxy<IQuestManager>((method, args) =>
        {
            Assert.Equal(nameof(IQuestManager.ProgressUserQuest), method);
            Assert.Same(_client, args[0]);
            Assert.Equal(QuestType.ExploreFindItem, args[1]);
            Assert.Equal((int)item.Definition.Id, args[2]);
            Assert.Equal("1", item.LegacyDataString);
            Assert.Single(_client.Packets, packet => packet.Header == ServerPacketHeader.ItemUpdateComposer);
            progressed++;

            return null;
        });

        new FurnitureUseService(null!, quests).UseWall(_room, _client, new(item.Id, 2));

        Assert.Equal(1, progressed);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("roll")]
    [InlineData("gate")]
    [InlineData("wall")]
    public void StaleRoomInteractionDoesNotMutateOrCallTheQuest(string action)
    {
        var item = Furni(55, InteractionType.Scoreboard, WiredBoxType.None);
        item.ExtraData = new LegacyDataFormat { Data = "0" };
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null, item, 1, 1, 0, true, false, false));
        item.LegacyDataString = "9";
        _client.GetHabbo().CurrentRoom = null;
        _client.Packets.Clear();
        var service = new FurnitureUseService(null!, null!);

        switch (action)
        {
            case "off":
                service.TurnOffDice(_room, _client, item.Id);
                break;
            case "roll":
                service.RollDice(_room, _client, new(item.Id, 3));
                break;
            case "gate":
                service.UseOneWayGate(_room, _client, item.Id);
                break;
            case "wall":
                service.UseWall(_room, _client, new(item.Id, 3));
                break;
        }

        Assert.Equal("9", item.LegacyDataString);
        Assert.Empty(_client.Packets);
    }
}
