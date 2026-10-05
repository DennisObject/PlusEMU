using Plus.Communication.Packets.Incoming.Users;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task PlayerRespectHandlerFullyDecodesThenDelegates()
    {
        var calls = 0;
        var service = Proxy<IRoomRespectService>((method, args) =>
        {
            Assert.Equal("Respect", method);
            Assert.Same(_room, args[0]);
            Assert.Same(_client, args[1]);
            Assert.Equal(9, args[2]);
            calls++;
            return null;
        });
        var packet = ClientPacket(9);
        await new RespectUserEvent(service).Parse(_room, _client, packet);
        Assert.False(packet.HasDataRemaining());
        Assert.Equal(1, calls);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void PlayerRespectKeepsQuestAchievementRewardAndNotificationOrder()
    {
        LegacyRider();
        var target = RespectTarget();
        var stats = _client.GetHabbo().HabboStats;
        stats.DailyRespectPoints = 2;
        _room.RespectNotificationsEnabled = true;
        var order = new List<string>();
        var service = new RoomRespectService(
            Proxy<IAchievementManager>((_, args) =>
            {
                Assert.Equal(2, stats.DailyRespectPoints);
                Assert.Equal(0, target.HabboStats.Respect);
                order.Add((string)args[1]);
                return null;
            }),
            Proxy<IQuestManager>((_, args) =>
            {
                Assert.Equal(QuestType.SocialRespect, args[1]);
                order.Add("quest");
                return null;
            }),
            Proxy<IRewardTrackManager>((_, args) =>
            {
                Assert.Equal(RewardTrackActions.GiveRespect, args[1]);
                Assert.Equal(1, stats.DailyRespectPoints);
                Assert.Equal(0, stats.RespectGiven);
                Assert.Empty(_client.Sent);
                order.Add("reward");
                return null;
            }));
        service.Respect(_room, _client, 9);
        Assert.Equal(new[] { "quest", "ACH_RespectGiven", "ACH_RespectEarned", "reward" }, order);
        Assert.Equal((1, 1, 1), (stats.DailyRespectPoints, stats.RespectGiven, target.HabboStats.Respect));
        Assert.Equal(new[] { ServerPacketHeader.RespectNotificationComposer, ServerPacketHeader.ActionComposer }, _client.Sent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExhaustedPlayerRespectDoesNotProgressOrPublish(int available)
    {
        LegacyRider();
        var target = RespectTarget();
        _client.GetHabbo().HabboStats.DailyRespectPoints = available;
        DeniedPlayerRespectService().Respect(_room, _client, 9);
        Assert.Equal(available, _client.GetHabbo().HabboStats.DailyRespectPoints);
        Assert.Equal(0, target.HabboStats.Respect);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void PlayerRespectDeniesMissingSelfDepartedAndStaleRoomActors()
    {
        var target = RespectTarget();
        _client.GetHabbo().HabboStats.DailyRespectPoints = 2;
        var service = DeniedPlayerRespectService();
        service.Respect(_room, _client, 9); // no actor
        LegacyRider();
        service.Respect(_room, _client, 7); // self
        service.Respect(_room, _client, 99); // missing target
        target.CurrentRoom = null!;
        service.Respect(_room, _client, 9);
        target.CurrentRoom = _room;
        _client.GetHabbo().CurrentRoom = null!;
        service.Respect(_room, _client, 9);
        Assert.Equal(2, _client.GetHabbo().HabboStats.DailyRespectPoints);
        Assert.Equal(0, target.HabboStats.Respect);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void DisabledRespectNotificationsStillPublishTheActorAction()
    {
        LegacyRider();
        RespectTarget();
        _client.GetHabbo().HabboStats.DailyRespectPoints = 1;
        _room.RespectNotificationsEnabled = false;
        var service = new RoomRespectService(
            Proxy<IAchievementManager>((_, _) => null),
            Proxy<IQuestManager>((_, _) => null),
            Proxy<IRewardTrackManager>((_, _) => null));
        service.Respect(_room, _client, 9);
        Assert.Equal(ServerPacketHeader.ActionComposer, Assert.Single(_client.Sent));
    }

    private Habbo RespectTarget()
    {
        _client.GetHabbo().HabboStats = RespectStats(0);
        var targetClient = new TestClient();
        var target = new Habbo { Id = 9, Username = "target", CurrentRoom = _room, HabboStats = RespectStats(0) };
        targetClient.SetHabbo(target);
        Assert.True(LegacyUsers().TryAdd(9, new RoomUser(9, RoomId, 9, _room, targetClient)));
        _gameField.SetValue(null, Proxy<IGame>((method, _) => method == "get_ClientManager"
            ? Proxy<IGameClientManager>((_, args) => (int)args[0] == 9 ? targetClient : _client) : null));
        return target;
    }

    private RoomRespectService DeniedPlayerRespectService() => new(
        Proxy<IAchievementManager>((_, _) => throw new InvalidOperationException("Denied respect must not progress achievements")),
        Proxy<IQuestManager>((_, _) => throw new InvalidOperationException("Denied respect must not progress quests")),
        Proxy<IRewardTrackManager>((_, _) => throw new InvalidOperationException("Denied respect must not progress rewards")));
}
