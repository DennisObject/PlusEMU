using Plus.Communication.Packets.Incoming.Rooms.Engine;
using Plus.Communication.Packets.Outgoing;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task PlacementHandlerReadsOnlyThePlacementStringAndDelegates()
    {
        var service = Proxy<IRoomItemPlacementService>((method, args) =>
        {
            Assert.Equal("Place", method);
            Assert.Same(_room, args[0]);
            Assert.Same(_client, args[1]);
            Assert.Equal("30 1 2 0", args[2]);
            return null;
        });
        var packet = ClientPacket("30 1 2 0");
        await new PlaceObjectEvent(service).Parse(_room, _client, packet);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(_client.Sent);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("30")]
    [InlineData("30 x 1 0")]
    public void InvalidFloorPlacementDoesNotConsumeInventoryOrPublishProgress(string placement)
    {
        var inventory = new InventoryItem { Id = 30, Definition = Furni(30, InteractionType.None, WiredBoxType.None).Definition };
        Inventory(inventory);
        PlacementService(() => throw new InvalidOperationException("Invalid input must not progress rewards"))
            .Place(_room, _client, placement);
        Assert.Same(inventory, _client.GetHabbo().Inventory.Furniture.GetItem(30));
        Assert.Empty(_room.GetRoomItemHandler().GetWallAndFloor);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void PlacementWithoutRoomRightsSendsTheExistingErrorAndKeepsInventory()
    {
        var inventory = new InventoryItem { Id = 30, Definition = Furni(30, InteractionType.None, WiredBoxType.None).Definition };
        Inventory(inventory);
        _room.OwnerName = "another-owner";
        _room.OwnerId = 99;
        _room.UsersWithRights = [];
        PlacementService(() => throw new InvalidOperationException("Denied placement must not progress rewards"))
            .Place(_room, _client, "30 1 1 0");
        Assert.Same(inventory, _client.GetHabbo().Inventory.Furniture.GetItem(30));
        Assert.Empty(_room.GetRoomItemHandler().GetWallAndFloor);
        Assert.Equal(ServerPacketHeader.RoomNotificationComposer, Assert.Single(_client.Sent));
    }

    [Theory]
    [InlineData(ItemType.Floor, "30 1 1 0")]
    [InlineData(ItemType.Wall, "30 :w=1,1 l=0,0 l")]
    public void SuccessfulPlacementRemovesInventoryBeforeAchievementsAndRewards(ItemType type, string placement)
    {
        Inventory(new InventoryItem { Id = 30, Definition = Furni(30, InteractionType.None, WiredBoxType.None, type).Definition });
        var progress = new List<string>();
        var achievements = Proxy<IAchievementManager>((method, args) =>
        {
            Assert.Equal("ProgressAchievement", method);
            Assert.Equal("ACH_RoomDecoFurniCount", args[1]);
            Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(30));
            Assert.Contains(ServerPacketHeader.FurniListRemoveComposer, _client.Sent);
            progress.Add("achievement");
            return null;
        });
        new RoomItemPlacementService(Proxy<ISettingsManager>((_, _) => "500"), achievements,
            Proxy<IRewardTrackManager>((method, args) =>
            {
                Assert.Equal("Progress", method);
                Assert.Equal(RewardTrackActions.PlaceItem, args[1]);
                progress.Add("reward");
                return null;
            }), Proxy<IQuestManager>((_, _) => null), TestLogging.For<RoomItemPlacementService>()).Place(_room, _client, placement);
        Assert.Equal(new[] { "achievement", "reward" }, progress);
        Assert.NotNull(_room.GetRoomItemHandler().GetItem(30));
        Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(30));
    }

    [Fact]
    public async Task MoveHandlerConsumesAllFourPrimitivesBeforeDelegation()
    {
        var calls = 0;
        var placement = Proxy<IRoomItemPlacementService>((method, args) =>
        {
            Assert.Equal("Move", method);
            Assert.Same(_room, args[0]);
            Assert.Same(_client, args[1]);
            Assert.Equal(new object[] { 30u, 2, 3, 4 }, args.Skip(2));
            calls++;
            return null;
        });
        var packet = ClientPacket(30, 2, 3, 4);
        await new MoveObjectEvent(placement).Parse(_room, _client, packet);
        Assert.False(packet.HasDataRemaining());
        Assert.Equal(1, calls);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void MoveRetainsQuestBeforePlacementAndRewardAfterPlacementOrder()
    {
        var item = Add(30, 1, 1);
        var calls = new List<string>();
        var quests = Proxy<IQuestManager>((_, args) =>
        {
            var kind = (QuestType)args[1]!;
            if (kind == QuestType.FurniMove || kind == QuestType.FurniRotate)
                Assert.Equal((1, 1, 0), (item.GetX, item.GetY, item.Rotation));
            calls.Add(kind.ToString());
            return null;
        });
        var rewards = Proxy<IRewardTrackManager>((_, args) =>
        {
            Assert.Equal((2, 2, 2), (item.GetX, item.GetY, item.Rotation));
            calls.Add((string)args[1]!);
            return null;
        });
        new RoomItemPlacementService(Proxy<ISettingsManager>((_, _) => "500"),
            Proxy<IAchievementManager>((_, _) => null), rewards, quests,
            TestLogging.For<RoomItemPlacementService>()).Move(_room, _client, item.Id, 2, 2, 2);
        Assert.Equal(new[] { "FurniMove", "FurniRotate", RewardTrackActions.MoveItem, RewardTrackActions.RotateItem }, calls);
    }

    private static RoomItemPlacementService PlacementService(Action reward) => new(
        Proxy<ISettingsManager>((_, _) => "500"), Proxy<IAchievementManager>((_, _) => null),
        Proxy<IRewardTrackManager>((_, _) => { reward(); return null; }), Proxy<IQuestManager>((_, _) => null), TestLogging.For<RoomItemPlacementService>());
}
