using System.Reflection;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Games.Banzai;
using Plus.HabboHotel.Rooms.Games.Teams;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdleRoomUsesItsInjectedOwnerAtSixtyCycles(bool v2)
    {
        var unloaded = new List<uint>();
        Set("_rooms", TestRoomOwners.Create(unloaded.Add));

        if (v2)
        {
            _room.EnableV2Movement();
        }

        _room.IdleTime = 58;
        WithUnavailableRoomGame(() =>
        {
            _room.ProcessRoom();
            Assert.Empty(unloaded);
            _room.ProcessRoom();
        });
        Assert.Equal([RoomId], unloaded);
        Assert.Equal(60, _room.IdleTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PromotedIdleRoomKeepsItsInjectedOwnerLoaded(bool v2)
    {
        var unloaded = new List<uint>();
        Set("_rooms", TestRoomOwners.Create(unloaded.Add));

        if (v2)
        {
            _room.EnableV2Movement();
        }

        _room.IdleTime = 59;
        var now = _interactionClock.GetUtcNow();
        _room.Promotion = new RoomPromotion("test", "", 1, now, now.AddHours(1), _interactionClock);
        WithUnavailableRoomGame(_room.ProcessRoom);
        Assert.Empty(unloaded);
        Assert.Equal(60, _room.IdleTime);
    }

    [Fact]
    public void FootballUsesInjectedAchievementBeforeItsActionPacket()
    {
        Assert.True(_room.GetRoomUserManager().AddAvatarToRoom(_client));
        var actor = _room.GetRoomUserManager().GetRoomUserByHabbo(7)!;
        var goal = Furni(96, InteractionType.None, WiredBoxType.None);
        goal.Definition.ItemName = "fball_goal_blue";
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, goal, 1, 1, 0, true, false, false));
        var ball = Furni(97, InteractionType.None, WiredBoxType.None);
        ball.SetState(1, 1, 0, new());
        _client.Sent.Clear();
        var calls = new List<string>();
        Set("_achievements", new TestRoomAchievements((session, group, amount) =>
        {
            Assert.Same(_client, session);
            Assert.Equal(1, amount);
            Assert.Empty(_client.Sent);
            calls.Add(group);
        }));
        WithUnavailableRoomGame(() => _room.OnUserShoot(actor, ball));
        Assert.Equal(["ACH_FootballGoalScored"], calls);
        Assert.Equal([ServerPacketHeader.ActionComposer], _client.Sent);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void BanzaiUsesInjectedAchievementsAfterStrictPlayBoundary(int extraTicks, bool awarded)
    {
        Assert.True(_room.GetRoomUserManager().AddAvatarToRoom(_client));
        var actor = _room.GetRoomUserManager().GetRoomUserByHabbo(7)!;
        _client.GetHabbo().Effects = new(_interactionClock)
        {
            CurrentEffect = 35
        };
        actor.Team = Team.Blue;
        actor.LockedTilesCount = 4;
        var calls = new List<(string, int)>();
        Set("_achievements", new TestRoomAchievements((session, group, amount) =>
        {
            Assert.Same(_client, session);
            calls.Add((group, amount));
        }));
        var banzai = _room.GetBanzai();
        typeof(BattleBanzai).GetField("_startedAt", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(banzai, _interactionClock.GetUtcNow().AddSeconds(-5).AddTicks(-extraTicks));
        _room.GetGameManager().Points[(int)Team.Blue] = 1;
        WithUnavailableRoomGame(() => banzai.BanzaiEnd(true));
        Assert.Equal(awarded ? [("ACH_BattleBallTilesLocked", 4), ("ACH_BattleBallPlayer", 1), ("ACH_BattleBallWinner", 1)] : [], calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoomEntryUsesLoadedAchievementDependencyAfterRecordedVisit(bool owner)
    {
        _room.OwnerId = owner ? 7 : 99;
        var habbo = _client.GetHabbo();
        habbo.Client = _client;
        habbo.HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        var events = new List<string>();
        habbo.SetRoomVisitRecorder(new RecordingEntry(events), new TestRoomAchievements((session, group, amount) =>
        {
            Assert.Same(_client, session);
            Assert.Equal("ACH_RoomEntry", group);
            Assert.Equal(1, amount);
            Assert.Equal(["visit"], events);
            Assert.Equal(1, habbo.HabboStats.RoomVisits);
            Assert.Equal(ServerPacketHeader.RoomRatingComposer, _client.Sent.Last());
            events.Add("achievement");
        }));
        WithUnavailableRoomGame(() => Assert.True(habbo.EnterRoom(_room)));
        Assert.Equal(owner ? ["visit"] : ["visit", "achievement"], events);
        Assert.Equal(owner ? 0 : 1, habbo.HabboStats.RoomVisits);
    }

    private sealed class RecordingEntry(List<string> events) : IRoomVisitRecorder
    {
        public void RecordEntry(int userId, uint roomId)
        {
            Assert.Equal(7, userId);
            Assert.Equal(RoomId, roomId);
            events.Add("visit");
        }
    }

    private void WithUnavailableRoomGame(Action action)
    {
        var previous = _gameField.GetValue(null);
        _gameField.SetValue(null, null);

        try
        {
            action();
        }
        finally { _gameField.SetValue(null, previous); }
    }
}
