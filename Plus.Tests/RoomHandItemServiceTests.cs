using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void GivingCoffeeProgressesBeforeTransferAndClearsTheGiversHand()
    {
        var actor = LegacyRider();
        actor.CarryItemId = 8;
        actor.CarryTimer = 20;
        var target = new RoomUser(9, RoomId, 9, _room) { X = actor.X + 2, Y = actor.Y + 2, DanceId = 1 };
        AddHandTarget(target);
        var quests = 0;
        var service = new RoomAvatarActionService(TimeProvider.System, Proxy<IQuestManager>((_, args) =>
        {
            Assert.Equal(QuestType.GiveCoffee, args[1]);
            Assert.Equal(8, actor.CarryItemId);
            Assert.Equal(0, target.CarryItemId);
            Assert.Empty(_client.Sent);
            quests++;
            return null;
        }), null!);
        service.GiveHandItem(_room, _client, 9);
        Assert.Equal(1, quests);
        Assert.Equal((0, 0, 8, 240, 0), (actor.CarryItemId, actor.CarryTimer, target.CarryItemId, target.CarryTimer, target.DanceId));
        Assert.Equal(new[] { ServerPacketHeader.CarryObjectComposer, ServerPacketHeader.CarryObjectComposer }, _client.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandItemDistanceGateKeepsTheModeratorOverride(bool moderator)
    {
        var actor = LegacyRider();
        actor.CarryItemId = 7;
        actor.CarryTimer = 20;
        var target = new RoomUser(9, RoomId, 9, _room) { X = actor.X + 3, Y = actor.Y };
        AddHandTarget(target);
        _client.GetHabbo().Access = EditorTestSupport.Access(moderator ? [PermissionKeys.ModerationTool] : []);
        new RoomAvatarActionService(TimeProvider.System, null!, null!).GiveHandItem(_room, _client, 9);
        Assert.Equal(moderator ? 7 : 0, target.CarryItemId);
        Assert.Equal(moderator ? 0 : 7, actor.CarryItemId);
        Assert.Equal(moderator ? 2 : 0, _client.Sent.Count);
    }

    private void AddHandTarget(RoomUser target)
    {
        var client = new TestClient();
        client.SetHabbo(new Plus.HabboHotel.Users.Habbo { Id = 9, Username = "target", CurrentRoom = _room });
        typeof(RoomUser).GetField("_mClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(target, client);
        Assert.True(LegacyUsers().TryAdd(9, target));
    }

    [Fact]
    public void MissingTargetExpiredCarryAndStaleRoomNeverGiveTheItem()
    {
        var actor = LegacyRider();
        actor.CarryItemId = 8;
        var target = new RoomUser(9, RoomId, 9, _room);
        AddHandTarget(target);
        var service = new RoomAvatarActionService(TimeProvider.System, null!, null!);
        service.GiveHandItem(_room, _client, 99);
        service.GiveHandItem(_room, _client, 9);
        actor.CarryTimer = 20;
        _client.GetHabbo().CurrentRoom = null!;
        service.GiveHandItem(_room, _client, 9);
        Assert.Equal(8, actor.CarryItemId);
        Assert.Equal(0, target.CarryItemId);
        Assert.Empty(_client.Sent);
    }
}
