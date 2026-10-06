using System.Reflection;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void CommitServiceRunsEffectLandingWhileMvIsStillVisible()
    {
        var item = Add(10, 1, 1, type: InteractionType.Effect);
        item.Definition.EffectId = 17;
        item.Definition.Walkable = true;
        InitializeNativeState(item);
        var actor = ExecutorActor(0, 1);
        InitializeClientEffects();
        actor.MoveTo(1, 1);
        ExecutorTick();
        Assert.Equal(0, _client.GetHabbo().Effects.CurrentEffect);
        ExecutorTick();
        Assert.Equal(17, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal("1", item.LegacyDataString);
        Assert.False(actor.HasStatus("mv"));
    }

    [Fact]
    public void AnnounceServiceAppliesTheTargetFloorEffect()
    {
        var actor = ExecutorActor(0, 1);
        InitializeClientEffects();
        _room.GetGameMap().EffectMap[1, 1] = 3;
        _client.GetHabbo().Gender = "M";
        actor.MoveTo(1, 1);
        ExecutorTick();
        Assert.True(actor.HasStatus("mv"));
        Assert.Equal(38, _client.GetHabbo().Effects.CurrentEffect);
    }
    [Fact]
    public void AnnounceServiceUsesInjectedRewardsOncePerSwimTransition()
    {
        var rewards = new RecordingNavigationRewards();
        var current = typeof(RewardTrackManager).GetField("<Current>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = current.GetValue(null);
        current.SetValue(null, null);

        try {
            var actor = ExecutorActor(0, 1, rewards: rewards);
            InitializeClientEffects();
            var effects = _room.GetGameMap().EffectMap;
            effects[1, 1] = effects[2, 1] = 1;

            actor.MoveTo(1, 1);
            ExecutorTick();
            Assert.True(actor.HasStatus("mv"));
            Assert.Equal(29, _client.GetHabbo().Effects.CurrentEffect);
            Assert.Equal([(_client, RewardTrackActions.Swim, 1)], rewards.Calls);
            ExecutorTick();
            actor.MoveTo(2, 1);
            ExecutorTick();
            ExecutorTick();
            Assert.Equal([(_client, RewardTrackActions.Swim, 1)], rewards.Calls);

            effects[3, 1] = 0;
            actor.MoveTo(3, 1);
            ExecutorTick();
            ExecutorTick();
            actor.MoveTo(2, 1);
            ExecutorTick();
            ExecutorTick();
            Assert.Equal([(_client, RewardTrackActions.Swim, 1), (_client, RewardTrackActions.Swim, 1)], rewards.Calls);
        }
        finally {
            current.SetValue(null, previous);
        }
    }

    [Fact]
    public void AnnounceServiceArrowUsesInjectedDatabaseWhenGlobalDatabaseIsUnavailable()
    {
        var arrow = ExecutorFloor(10, 1, 1);
        arrow.Definition.InteractionType = InteractionType.Arrow;
        Add(11, 3, 2, z: 0.75, type: InteractionType.Teleport);
        var previous = _databaseField.GetValue(null);
        _databaseField.SetValue(null, TestNavigationDatabase.Instance);

        try {
            var actor = ExecutorActor(0, 1, database: LandingDatabase(11, RoomId));
            actor.MoveTo(1, 1);
            ExecutorTick();
            ExecutorTick();
            Assert.Equal((3, 2, 0.75), (actor.X, actor.Y, actor.Z));
        }
        finally {
            _databaseField.SetValue(null, previous);
        }
    }
    private void InitializeClientEffects()
    {
        var habbo = _client.GetHabbo();
        habbo.Client = _client;
        habbo.Effects.Init(habbo);
    }

    private sealed class RecordingNavigationRewards : IRewardTrackManager
    {
        public List<(GameClient Client, string Action, int Amount)> Calls { get; } = [];
        public void Progress(GameClient session, string actionType, int amount = 1) =>
            Calls.Add((session, actionType, amount));
        public void SendTracks(GameClient session) => throw new NotSupportedException();
        public Task Claim(GameClient session, string trackId, string prizeId) => throw new NotSupportedException();
        public void PurchasePremium(GameClient session, string trackId) => throw new NotSupportedException();
    }
}
