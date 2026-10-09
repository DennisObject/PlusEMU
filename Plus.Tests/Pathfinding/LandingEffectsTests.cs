using System.Collections.Concurrent;
using System.Data;
using System.Runtime.CompilerServices;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.Games.Freeze;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(InteractionType.Banzaigateblue, Team.Blue, 32)]
    [InlineData(InteractionType.Banzaigatered, Team.Red, 32)]
    [InlineData(InteractionType.Banzaigategreen, Team.Green, 32)]
    [InlineData(InteractionType.Banzaigateyellow, Team.Yellow, 32)]
    [InlineData(InteractionType.FreezeBlueGate, Team.Blue, 39)]
    [InlineData(InteractionType.FreezeRedGate, Team.Red, 39)]
    [InlineData(InteractionType.FreezeGreenGate, Team.Green, 39)]
    [InlineData(InteractionType.FreezeYellowGate, Team.Yellow, 39)]
    public void LandingGateJoinsAndLeavesTheSameTeam(InteractionType kind, Team team, int offset)
    {
        var gate = Add(10, 1, 1, type: kind);
        gate.Team = team;
        InitializeNativeState(gate);
        var actor = LandingActor();
        var effects = new LandingEffects(_room, _database);
        effects.Apply(actor, false);
        Assert.Equal(team, actor.Team);
        Assert.Equal((int)team + offset, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal("1", gate.LegacyDataString);
        effects.Apply(actor, false);
        Assert.Equal(Team.None, actor.Team);
        Assert.Equal(0, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal("0", gate.LegacyDataString);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingDifferentTeamGateLeavesWithoutJoining(bool freeze)
    {
        var gate = Add(10, 1, 1, type: freeze ? InteractionType.FreezeRedGate : InteractionType.Banzaigatered);
        gate.Team = Team.Red;
        var actor = LandingActor();
        actor.Team = Team.Blue;
        var teams = freeze ? _room.GetTeamManagerForFreeze() : _room.GetTeamManagerForBanzai();
        teams.AddUser(actor);
        _client.GetHabbo().Effects.ApplyEffect(12);
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.Equal(Team.None, actor.Team);
        Assert.Empty(teams.BlueTeam);
        Assert.Empty(teams.RedTeam);
        Assert.Equal(0, _client.GetHabbo().Effects.CurrentEffect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingFullTeamGatePreservesActorAndEffect(bool freeze)
    {
        var gate = Add(10, 1, 1, type: freeze ? InteractionType.FreezeBlueGate : InteractionType.Banzaigateblue);
        gate.Team = Team.Blue;
        var actor = LandingActor();
        var teams = freeze ? _room.GetTeamManagerForFreeze() : _room.GetTeamManagerForBanzai();

        for (var id = 2; id <= 6; id++) {
            teams.BlueTeam.Add(new RoomUser(id, RoomId, id, _room, null, TestChatEmotions.Unused, TestRewardProgress.Unused));
        }

        _client.GetHabbo().Effects.ApplyEffect(12);
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.Equal(Team.None, actor.Team);
        Assert.Equal(5, teams.BlueTeam.Count);
        Assert.Equal(12, _client.GetHabbo().Effects.CurrentEffect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingBanzaiTeleportRequiresVisibleMv(bool moving)
    {
        Add(10, 1, 1, type: InteractionType.Banzaitele);
        var target = Add(11, 3, 1, z: 0.75, type: InteractionType.Banzaitele);
        InitializeNativeState(target);
        var actor = LandingActor();

        if (moving) {
            actor.Statusses.Add("mv", "1,1,0");
        }

        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.Equal(moving ? (3, 1, 0.75) : (1, 1, 0d), (actor.X, actor.Y, actor.Z));

        if (moving) {
            Assert.Equal("1", target.LegacyDataString);
        }
    }

    [Fact]
    public void LandingEffectActivatesItemAndSchedulesReset()
    {
        var item = Add(10, 1, 1, type: InteractionType.Effect);
        item.Definition.EffectId = 17;
        InitializeNativeState(item);
        var actor = LandingActor();
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.Equal(17, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal("1", item.LegacyDataString);
        Assert.True(item.UpdateNeeded);
    }

    [Fact]
    public void LandingZeroEffectPreservesInactiveItem()
    {
        var item = Add(10, 1, 1, type: InteractionType.Effect);
        InitializeNativeState(item);
        item.LegacyDataString = "0";
        var actor = LandingActor();
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.Equal(0, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal("0", item.LegacyDataString);
    }

    [Fact]
    public void LandingArrowTeleportsOnlyAtTheRequestedGoalAndSkipsGameWalk()
    {
        Add(10, 1, 1, type: InteractionType.Arrow);
        Add(11, 3, 2, z: 0.75, type: InteractionType.Teleport);
        var ball = Add(12, 1, 1);
        _room.GetSoccer().AddBall(ball);
        var actor = LandingActor();
        actor.RotBody = 2;
        actor.GoalX = 2;
        var effects = new LandingEffects(_room, LandingDatabase(11, RoomId));
        effects.Apply(actor, false);
        Assert.Equal((1, 1), (actor.X, actor.Y));
        _room.GetSoccer().RemoveBall(ball.Id);
        actor.GoalX = 1;
        _room.GetSoccer().AddBall(ball);
        effects.Apply(actor, false);
        Assert.Equal((3, 2, 0.75), (actor.X, actor.Y, actor.Z));
        Assert.Equal((3, 1), (ball.GetX, ball.GetY));
    }

    [Fact]
    public void LandingUnlinkedArrowUnlocksWalking()
    {
        Add(10, 1, 1, type: InteractionType.Arrow);
        var actor = LandingActor();
        actor.AllowOverride = false;
        actor.CanWalk = false;
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.True(actor.CanWalk);
    }

    [Fact]
    public void LandingCrossRoomArrowSetsTeleportFlagsAndTerminatesBeforeGameWalk()
    {
        Add(10, 1, 1, type: InteractionType.Arrow);
        var ball = Add(12, 1, 1);
        _room.GetSoccer().AddBall(ball);
        var actor = LandingActor();
        actor.RotBody = 2;
        uint prepared = 0;
        new LandingEffects(_room, LandingDatabase(11, 99), (habbo, roomId) =>
        { prepared = roomId; habbo.CurrentRoom = null!; }).Apply(actor, false);
        Assert.Equal(99u, prepared);
        var habbo = _client.GetHabbo();
        Assert.True(habbo.IsTeleporting);
        Assert.Equal(99u, habbo.TeleportingRoomId);
        Assert.Equal(11u, habbo.TeleporterId);
        Assert.Equal((1, 1), (ball.GetX, ball.GetY));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingRunsExistingSoccerAndBanzaiWalkHooks(bool banzai)
    {
        var ball = Add(10, 1, 1);

        if (banzai) {
            _room.GetBanzai().AddPuck(ball);
        }
        else {
            _room.GetSoccer().AddBall(ball);
        }

        var actor = LandingActor();
        actor.RotBody = 2;
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.Equal((3, 1), (ball.GetX, ball.GetY));
    }

    [Fact]
    public void LandingRunsExistingFreezePowerUpPickup()
    {
        var block = Add(10, 1, 1);
        var freeze = _room.GetFreeze();
        freeze.AddFreezeBlock(block);
        var actor = LandingActor();
        actor.Team = Team.Blue;
        freeze.StartGame();
        block.FreezePowerUp = FreezePowerUp.BlueArrow;
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.Equal(FreezePowerUp.BlueArrow, actor.BanzaiPowerUp);
        Assert.Equal(FreezePowerUp.None, block.FreezePowerUp);
    }

    [Fact]
    public void LandingPreservesSupportPostureWithoutScanningLowerSeats()
    {
        Add(10, 1, 1, type: InteractionType.Bed);
        Add(11, 1, 1, z: 0.5, seat: true);
        var actor = LandingActor();
        actor.Z = 2.125;
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.False(actor.HasStatus("sit"));
        Assert.False(actor.HasStatus("lay"));
        Assert.Equal(2.125, actor.Z);
    }

    [Theory]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 0)]
    public void LandingLayActionFiresOnlyOnNewSupportPosture(bool wasLaying, bool laying, int expected)
    {
        var action = LandingLayCounter();
        var actor = LandingActor();

        if (laying) {
            actor.Statusses.Add("lay", "0.5 null");
        }

        new LandingEffects(_room, _database).Apply(actor, wasLaying);
        _room.GetWired().OnFastCycle();
        _room.GetWired().OnFastCycle();
        Assert.Equal(expected, action.Calls);
    }

    [Fact]
    public void LandingBotSkipsTeamEffectAndGameWalkChanges()
    {
        var gate = Add(10, 1, 1, type: InteractionType.Banzaigateblue);
        gate.Team = Team.Blue;
        var effect = Add(11, 1, 1, type: InteractionType.Effect);
        effect.Definition.EffectId = 17;
        var ball = Add(12, 1, 1);
        _room.GetSoccer().AddBall(ball);
        var actor = LandingActor();
        actor.RotBody = 2;
        actor.BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        new LandingEffects(_room, _database).Apply(actor, false);
        Assert.Equal(Team.None, actor.Team);
        Assert.Equal(0, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal((1, 1), (ball.GetX, ball.GetY));
    }

    [Theory]
    [InlineData(1, "M", 29, ItemEffectType.Swim)]
    [InlineData(2, "M", 55, ItemEffectType.Normalskates)]
    [InlineData(2, "F", 56, ItemEffectType.Normalskates)]
    [InlineData(3, "M", 38, ItemEffectType.Iceskates)]
    [InlineData(3, "F", 39, ItemEffectType.Iceskates)]
    [InlineData(4, "M", 30, ItemEffectType.SwimLow)]
    [InlineData(5, "M", 37, ItemEffectType.SwimHalloween)]
    public void FloorEffectAppliesLegacyMapAndGenderMapping(byte mapValue, string gender, int effect, ItemEffectType kind)
    {
        var actor = LandingActor();
        _client.GetHabbo().Gender = gender;
        _room.GetGameMap().EffectMap[2, 1] = mapValue;
        new FloorEffectService(_room, _ => { }).Apply(actor, 2, 1);
        Assert.Equal(effect, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal(kind, actor.CurrentItemEffect);
        Assert.Equal((1, 1), (actor.X, actor.Y));
    }

    [Fact]
    public void FloorEffectSwimProgressOccursOnceUntilEffectIsCleared()
    {
        var actor = LandingActor();
        _room.GetGameMap().EffectMap[1, 1] = 1;
        var progress = 0;
        var service = new FloorEffectService(_room, client => { Assert.Same(_client, client); progress++; });
        service.Apply(actor, 1, 1);
        service.Apply(actor, 1, 1);
        Assert.Equal(1, progress);
        _client.GetHabbo().Effects.ApplyEffect(0);
        service.Apply(actor, 1, 1);
        Assert.Equal(2, progress);
        Assert.Equal(29, _client.GetHabbo().Effects.CurrentEffect);
    }

    [Fact]
    public void FloorEffectClearResetsOnlyAnExistingFloorEffect()
    {
        var actor = LandingActor();
        var service = new FloorEffectService(_room, _ => { });
        _client.GetHabbo().Effects.ApplyEffect(17);
        service.Apply(actor, 1, 1);
        Assert.Equal(17, _client.GetHabbo().Effects.CurrentEffect);
        actor.CurrentItemEffect = ItemEffectType.Swim;
        service.Apply(actor, 1, 1);
        Assert.Equal(-1, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal(ItemEffectType.None, actor.CurrentItemEffect);
    }

    [Fact]
    public void FloorEffectBotSkipsEffectAndSwimProgress()
    {
        var actor = LandingActor();
        actor.BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
        _room.GetGameMap().EffectMap[1, 1] = 1;
        new FloorEffectService(_room, _ => throw new InvalidOperationException("Bot progress")).Apply(actor, 1, 1);
        Assert.Equal(0, _client.GetHabbo().Effects.CurrentEffect);
        Assert.Equal(ItemEffectType.None, actor.CurrentItemEffect);
    }

    private RoomUser LandingActor()
    {
        var actor = Viewer(1, 1);
        actor.GoalX = actor.X;
        actor.GoalY = actor.Y;
        var habbo = _client.GetHabbo();
        habbo.Client = _client;
        habbo.Gender = "M";
        habbo.Effects.Init(habbo);

        return actor;
    }

    private static void InitializeNativeState(Item item)
        => item.ExtraData = FurniExtraData.Load(item.Definition, "0", keepLegacy: true);

    private IDatabase LandingDatabase(uint linkedId, uint targetRoom)
    {
        return Proxy<IDatabase>((method, _) => method switch
        {
            "Connection" => new NoOpConnection(command =>
            {
                var result = new DataTable();
                result.Columns.Add("value", typeof(uint));
                result.Rows.Add(command.Contains("room_items_tele_links", StringComparison.Ordinal) ? linkedId : targetRoom);

                return result;
            }),
            _ => throw new NotSupportedException(method)
        });
    }

    private LandingActionCounter LandingLayCounter()
    {
        var triggerItem = Add(900, 3, 3, type: InteractionType.WiredTrigger);
        triggerItem.Definition.InteractionName = "wf_trg_user_performs_action";
        var wired = _room.GetWired();
        var trigger = wired.CreateConfiguredBox(triggerItem)!;
        Assert.True(trigger.TryValidateConfiguration(new() { IntParams = [(int)WiredAvatarAction.Lay, 0, 0, 0, 1] }, out var config, out var error), error);
        trigger.ApplyConfiguration(config);
        Assert.True(wired.AddBox(trigger));
        var item = Add(901, 3, 3, type: InteractionType.WiredEffect);
        var action = new LandingActionCounter(_room, item);
        Assert.True(wired.AddBox(action));

        return action;
    }

    private sealed class LandingActionCounter(Room room, Item item) : IWiredContextualAction
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.EffectShowMessage;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public int Calls;
        public WiredBoxDescriptor Descriptor { get; } = new("test", WiredBoxCategory.Action, 0, 0, "test") { Support = WiredBoxSupport.Implemented };
        public WiredConfiguration Configuration { get; private set; } = new();
        public bool IsNegative => false;
        public bool Execute(params object[] arguments) => throw new NotSupportedException();
        public bool Execute(WiredRuntimeContext context)
        {
            Calls++;

            return true;
        }
        public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
        {
            validated = proposed;
            error = "";

            return true;
        }
        public void ApplyConfiguration(WiredConfiguration validated) => Configuration = validated;
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
    }
}
