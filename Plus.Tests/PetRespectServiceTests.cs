using Plus.Communication.Packets.Incoming.Rooms.AI.Pets;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public async Task RespectHandlerFullyDecodesAndOnlyDelegates()
    {
        var calls = 0;
        var service = Proxy<IPetRespectService>((method, args) =>
        {
            Assert.Equal("Respect", method);
            Assert.Same(_room, args[0]);
            Assert.Same(_client, args[1]);
            Assert.Equal(50, args[2]);
            calls++;

            return null;
        });
        var packet = ClientPacket(50);
        await new RespectPetEvent(service).Parse(_room, _client, packet);
        Assert.False(packet.HasDataRemaining());
        Assert.Equal(1, calls);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void PetRespectPreservesAchievementPetRewardAndCarryOrder()
    {
        var actor = LegacyRider();
        var pet = LegacyHorse(2, 1).PetData;
        _client.GetHabbo().HabboStats = RespectStats(2);
        var order = new List<string>();
        var service = RespectService((method, args) =>
        {
            Assert.Equal("ACH_PetRespectGiver", args[1]);
            Assert.Equal(1, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).DailyPetRespectPoints);
            Assert.Equal(0, pet.Respect);
            order.Add("achievement");
        }, (_, _) => throw new InvalidOperationException("Pet path must not progress the human quest"), () =>
        {
            Assert.Equal(1, pet.Respect);
            Assert.Equal(10, pet.Experience);
            Assert.Equal((999999999, 5), (actor.CarryItemId, actor.CarryTimer));
            Assert.Contains(ServerPacketHeader.RespectPetNotificationComposer, _client.Sent);
            Assert.DoesNotContain(ServerPacketHeader.CarryObjectComposer, _client.Sent);
            order.Add("reward");
        });
        service.Respect(_room, _client, 50);
        Assert.Equal(new[] { "achievement", "reward" }, order);
        Assert.Equal(ServerPacketHeader.CarryObjectComposer, _client.Sent.Last());
        Assert.Equal(PetDatabaseUpdateState.NeedsUpdate, pet.DbState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ExhaustedPetRespectDoesNotMutatePetOrPublish(int available)
    {
        LegacyRider();
        var pet = LegacyHorse(2, 1).PetData;
        _client.GetHabbo().HabboStats = RespectStats(available);
        DeniedRespectService().Respect(_room, _client, 50);
        Assert.Equal(0, pet.Respect);
        Assert.Equal(available, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).DailyPetRespectPoints);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void StaleRoomAndMissingActorOrTargetDoNotSpendRespect()
    {
        _client.GetHabbo().HabboStats = RespectStats(2);
        DeniedRespectService().Respect(_room, _client, 50);
        LegacyRider();
        DeniedRespectService().Respect(_room, _client, 99);
        var pet = LegacyHorse(2, 1).PetData;
        _client.GetHabbo().CurrentRoom = null!;
        DeniedRespectService().Respect(_room, _client, 50);
        Assert.Equal(0, pet.Respect);
        Assert.Equal(2, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).DailyPetRespectPoints);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public void HumanPetRespectKeepsQuestAchievementCounterAndNotificationOrder()
    {
        LegacyRider();
        _client.GetHabbo().HabboStats = RespectStats(2);
        var targetClient = new TestClient();
        var target = new Habbo { Id = 9, Username = "target", CurrentRoom = _room, HabboStats = RespectStats(0) };
        targetClient.SetHabbo(target);
        Assert.True(LegacyUsers().TryAdd(9, new RoomUser(9, RoomId, 9, _room, targetClient, TestChatEmotions.Unused, TestRewardProgress.Unused)));
        _room.RespectNotificationsEnabled = true;
        _gameField.SetValue(null, Proxy<IGame>((method, _) => method == "get_ClientManager"
            ? Proxy<IGameClientManager>((_, args) => (int)args[0] == 9 ? targetClient : _client) : null));
        var order = new List<string>();
        var service = RespectService((_, args) =>
        {
            Assert.Equal(2, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).DailyPetRespectPoints);
            order.Add((string)args[1]);
        }, (_, args) =>
        {
            Assert.Equal(QuestType.SocialRespect, args[1]);
            order.Add("quest");
        }, () => throw new InvalidOperationException("Human branch must not reward pet respect"));
        service.Respect(_room, _client, 9);
        Assert.Equal(new[] { "quest", "ACH_RespectGiven", "ACH_RespectEarned" }, order);
        Assert.Equal(1, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).DailyPetRespectPoints);
        Assert.Equal(1, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).RespectGiven);
        Assert.Equal(1, target.HabboStats.Respect);
        Assert.Equal(new[] { ServerPacketHeader.RespectPetNotificationComposer, ServerPacketHeader.CarryObjectComposer }, _client.Sent);
        target.CurrentRoom = null!;
        DeniedRespectService().Respect(_room, _client, 9);
        Assert.Equal(1, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).DailyPetRespectPoints);
    }

    [Fact]
    public void RespectingYourOwnHumanAvatarDoesNotSpendOrProgress()
    {
        LegacyRider();
        _client.GetHabbo().HabboStats = RespectStats(2);
        DeniedRespectService().Respect(_room, _client, 7);
        Assert.Equal(2, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).DailyPetRespectPoints);
        Assert.Equal(0, Assert.IsType<HabboStats>(_client.GetHabbo().HabboStats).RespectGiven);
        Assert.DoesNotContain(ServerPacketHeader.CarryObjectComposer, _client.Sent);
        Assert.DoesNotContain(ServerPacketHeader.RespectPetNotificationComposer, _client.Sent);
    }

    private PetRespectService DeniedRespectService() => RespectService(
        (_, _) => throw new InvalidOperationException("Denied respect must not progress achievements"),
        (_, _) => throw new InvalidOperationException("Denied respect must not progress quests"),
        () => throw new InvalidOperationException("Denied respect must not progress rewards"));

    private PetRespectService RespectService(Action<string, object?[]> achievement,
        Action<string, object?[]> quest, Action reward) => new(
        Proxy<IAchievementManager>((method, args) => { achievement(method, args); return null; }),
        Proxy<IQuestManager>((method, args) => { quest(method, args); return null; }),
        Proxy<IRewardTrackManager>((method, args) =>
        {
            Assert.Equal("Progress", method);
            Assert.Equal(RewardTrackActions.PetRespect, args[1]);
            reward();

            return null;
        }));

    private static HabboStats RespectStats(int available) => new(0, 0, 0, 0, 0, 0, 0, available, 0, 0, 0, 0, "old", 0);
}
