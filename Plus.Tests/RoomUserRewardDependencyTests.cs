using System.Reflection;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void AdmissionUsesInjectedVisitAndFollowRewardsInOrder(bool owner, bool matchingFollow)
    {
        _room.OwnerId = owner ? 7 : 99;
        var habbo = _client.GetHabbo();
        habbo.PendingFollowRoomId = matchingFollow ? RoomId : RoomId + 1;
        var rewards = new TestRewardProgress((session, action, _) =>
        {
            Assert.Same(_client, session);
            Assert.NotNull(_room.GetRoomUserManager().GetRoomUserByHabbo(7));
            Assert.Equal(action == RewardTrackActions.FollowFriend ? 0u : matchingFollow ? RoomId : RoomId + 1,
                habbo.PendingFollowRoomId);
        });
        var manager = InstallRewardManager(rewards);

        WithUnavailableRewardManager(() => Assert.True(manager.AddAvatarToRoom(_client)));

        var expected = new List<(GameClient Session, string Action, int Amount)>();

        if (!owner)
        {
            expected.Add((_client, RewardTrackActions.EnterOtherUsersRoom, 1));
        }

        if (matchingFollow)
        {
            expected.Add((_client, RewardTrackActions.FollowFriend, 1));
        }

        Assert.Equal(expected, rewards.Calls);
        Assert.Equal(0u, habbo.PendingFollowRoomId);
    }

    [Fact]
    public void TeleportArrivalRewardsBeforeTileResetThenVisitAndFollow()
    {
        _room.OwnerId = 99;
        var tile = Furni(95, InteractionType.Teleport, WiredBoxType.None);
        tile.ExtraData = new LegacyDataFormat();
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, tile, 1, 1, 0, true, false, false));
        var habbo = _client.GetHabbo();
        habbo.IsTeleporting = true;
        habbo.TeleporterId = tile.Id;
        habbo.PendingFollowRoomId = RoomId;
        var rewards = new TestRewardProgress((session, action, _) =>
        {
            Assert.Same(_client, session);

            if (action == RewardTrackActions.Teleport)
            {
                Assert.Equal("2", tile.LegacyDataString);
                Assert.Equal(0, tile.InteractingUser2);
            }
            else
            {
                Assert.Equal("0", tile.LegacyDataString);
            }
        });
        var manager = InstallRewardManager(rewards);

        WithUnavailableRewardManager(() => Assert.True(manager.AddAvatarToRoom(_client)));

        Assert.Equal([(_client, RewardTrackActions.Teleport, 1),
            (_client, RewardTrackActions.EnterOtherUsersRoom, 1), (_client, RewardTrackActions.FollowFriend, 1)], rewards.Calls);
        var actor = Assert.IsType<RoomUser>(manager.GetRoomUserByHabbo(7));
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Equal(7, tile.InteractingUser2);
        Assert.Equal("0", tile.LegacyDataString);
    }

    [Fact]
    public void LegacyCycleRewardsOnlyEachNewSwimTransitionWithGlobalUnavailable()
    {
        var rewards = new TestRewardProgress();
        var manager = InstallRewardManager(rewards);
        _client.GetHabbo().Effects = new(_interactionClock);
        InitializeClientEffects();
        WithUnavailableRewardManager(() =>
        {
            Assert.True(manager.AddAvatarToRoom(_client));
            var actor = Assert.IsType<RoomUser>(manager.GetRoomUserByHabbo(7));
            var effects = _room.GetGameMap().EffectMap;
            effects[1, 1] = effects[2, 1] = 1;
            actor.SetPos(1, 1, 0);
            manager.OnCycle();
            Assert.Equal(29, _client.GetHabbo().Effects.CurrentEffect);
            Assert.Equal([(_client, RewardTrackActions.Swim, 1)], rewards.Calls);
            manager.OnCycle();
            actor.SetPos(2, 1, 0);
            manager.OnCycle();
            Assert.Single(rewards.Calls);
            actor.SetPos(3, 1, 0);
            manager.OnCycle();
            Assert.Equal(-1, _client.GetHabbo().Effects.CurrentEffect);
            actor.SetPos(2, 1, 0);
            manager.OnCycle();
            Assert.Equal([(_client, RewardTrackActions.Swim, 1), (_client, RewardTrackActions.Swim, 1)], rewards.Calls);
        });
    }

    [Fact]
    public void RejectedAdmissionDoesNotRewardOrConsumePendingFollow()
    {
        var rewards = new TestRewardProgress();
        var manager = InstallRewardManager(rewards);
        _client.GetHabbo().CurrentRoom = null;
        _client.GetHabbo().PendingFollowRoomId = RoomId;

        WithUnavailableRewardManager(() => Assert.False(manager.AddAvatarToRoom(_client)));

        Assert.Empty(rewards.Calls);
        Assert.Equal(RoomId, _client.GetHabbo().PendingFollowRoomId);
        Assert.Null(manager.GetRoomUserByHabbo(7));
    }

    private RoomUserManager InstallRewardManager(TestRewardProgress rewards)
    {
        var manager = new RoomUserManager(_room, TestRoomUserStore.Instance, _interactionClock, rewards, TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel);
        Set("_roomUserManager", manager);

        return manager;
    }

    private static void WithUnavailableRewardManager(Action action)
    {
        var current = typeof(RewardTrackManager).GetField("<Current>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);

        try
        {
            current.SetValue(null, null);
            action();
        }
        finally { current.SetValue(null, previous); }
    }
}
