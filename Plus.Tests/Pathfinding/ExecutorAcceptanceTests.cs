using System.Collections.Concurrent;
using System.Reflection;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Users.Permissions;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void ExecutorPhaseOrderingCommitsEveryActorBeforeTheFirstNextAnnouncement()
    {
        var first = ExecutorActor(0, 1); var second = AcceptanceBot(0, 2, 2);
        ExecutorTick(); _client.GetHabbo().Effects.Init(_client.GetHabbo());
        _room.GetGameMap().EffectMap[2, 1] = 1;
        first.MoveTo(3, 1); second.MoveTo(3, 2); ExecutorTick();
        var observed = new List<(int FirstX, int SecondX, int FirstPending, int SecondPending)>();
        _client.BeforeCapture = header =>
        {
            if (header == ServerPacketHeader.AvatarEffectComposer)
                observed.Add((first.X, second.X, first.Movement.PendingCount, second.Movement.PendingCount));
        };
        ExecutorTick();
        Assert.Equal(new[] { (1, 1, 1, 0) }, observed);
        Assert.Equal("2,1,0", first.Statusses["mv"]);
        Assert.Equal("2,2,0", second.Statusses["mv"]);
    }

    [Fact]
    public void ExecutorHeadOnCorridorWaitsReplansAndStopsWithoutOverlapping()
    {
        Set("_gamemap", new Gamemap(_room, new RoomModel("corridor", 0, 0, 0, 0,
            "xxxx\r0000\rxxxx\rxxxx", 0, 0, false), TestLogging.Navigation));
        _room.GetGameMap().GenerateMaps();
        var first = ExecutorActor(0, 1); var second = AcceptanceBot(3, 1, 2); ExecutorTick();
        first.MoveTo(2, 1); second.MoveTo(1, 1); ExecutorTick();
        Assert.Equal(1, first.Movement.PendingCount); Assert.Equal(1, second.Movement.PendingCount);
        ExecutorTick();
        Assert.Equal((1, 2), (first.X, second.X));
        Assert.Equal(1, first.Movement.WaitTicks); Assert.Equal(1, second.Movement.WaitTicks);
        Assert.True(first.Movement.HasIntent); Assert.True(second.Movement.HasIntent);
        Assert.False(first.HasStatus("mv")); Assert.False(second.HasStatus("mv"));
        ExecutorTick();
        Assert.Equal(RouteState.Suspect, first.Movement.Fallback.State); Assert.Equal(RouteState.Suspect, second.Movement.Fallback.State);
        Assert.Equal(0, first.Movement.BlockReplans); Assert.Equal(0, second.Movement.BlockReplans);
        for (var tick = 0; tick < 12; tick++)
        { ExecutorTick(); Assert.NotEqual(first.Coordinate, second.Coordinate); }
        Assert.False(first.Movement.HasIntent); Assert.False(second.Movement.HasIntent);
        Assert.False(first.HasStatus("mv")); Assert.False(second.HasStatus("mv"));
    }

    [Fact]
    public void ExecutorSaturatedSearchBudgetKeepsHighIdAheadOfNewLowIdRequests()
    {
        var first = ExecutorConfiguredActor(new() { Engine = PathfindingEngine.V2,
            MaxExpansionsPerRoomTick = 1 });
        var second = AcceptanceBot(0, 2, 2); var high = AcceptanceBot(0, 3, 200); ExecutorTick();
        first.MoveTo(3, 1); second.MoveTo(3, 2); high.MoveTo(3, 3); ExecutorTick();
        Assert.True(first.HasStatus("mv")); Assert.False(second.HasStatus("mv"));
        Assert.False(high.HasStatus("mv")); Assert.True(high.Movement.HasIntent);
        first.MoveTo(3, 1); ExecutorTick();
        Assert.True(second.HasStatus("mv")); Assert.False(high.HasStatus("mv"));
        first.MoveTo(3, 1); ExecutorTick();
        Assert.Equal("1,3,0", high.Statusses["mv"]);
        Assert.Equal((0, 3), (high.X, high.Y));
        ExecutorTick();
        Assert.Equal((1, 3), (high.X, high.Y));
    }

    [Fact]
    public void ExecutorDeferredReplacementRemovesTheCommittedMvWhileItsSearchWaits()
    {
        var first = ExecutorConfiguredActor(new() { Engine = PathfindingEngine.V2,
            MaxExpansionsPerRoomTick = 1 });
        var second = AcceptanceBot(0, 2, 2); ExecutorTick();
        first.MoveTo(3, 1); second.MoveTo(3, 2); ExecutorTick();
        Assert.Equal("1,1,0", first.Statusses["mv"]);
        first.MoveTo(3, 1); ExecutorTick();
        Assert.Equal((1, 1), (first.X, first.Y)); Assert.True(first.Movement.HasIntent);
        Assert.Equal(0, first.Movement.Route.Count); Assert.Equal(0, first.Movement.PendingCount);
        Assert.False(first.HasStatus("mv"));
        Assert.DoesNotContain("/mv ", ExecutorUpdate(first).Status);
    }

    [Fact]
    public void ExecutorPendingAdmissionActorInTheRosterIsSkippedUntilAdmissionDrains()
    {
        ExecutorActor(0, 1);
        var pending = AcceptanceBot(0, 2, 8, admit: false);
        Assert.Same(pending, _room.GetRoomUserManager().GetRoomUserByVirtualId(8));
        pending.MoveTo(3, 2);
        for (var tick = 0; tick < 3; tick++) ExecutorTick();
        Assert.Equal(NavState.PendingAdmission, pending.Movement.State);
        Assert.Equal((0, 2), (pending.X, pending.Y)); Assert.Equal(0, pending.IdleTime);
        Assert.Equal(0, pending.Movement.ConsumedSequence);
        Assert.False(pending.HasStatus("mv")); Assert.Equal(0, pending.Movement.PendingCount);
        _room.GetGameMap().Navigation!.Admit(pending); ExecutorTick();
        Assert.Equal(NavState.Active, pending.Movement.State);
        Assert.Equal("1,2,0", pending.Statusses["mv"]);
        Assert.Equal(pending.Movement.Commands.Read()!.Sequence, pending.Movement.ConsumedSequence);
        ExecutorTick();
        Assert.Equal((1, 2), (pending.X, pending.Y));
    }

    [Theory]
    [InlineData(InteractionType.Banzaigateblue)]
    [InlineData(InteractionType.Banzaitele)]
    [InlineData(InteractionType.Teleport)]
    public void ExecutorFastWalkSkipsIntermediateInteractionHooksOnWalkMagic(InteractionType kind)
    {
        var source = ExecutorFloor(20, 0, 1); var target = ExecutorFloor(21, 3, 1);
        var skipped = InteractionItem(10, 1, 1, kind); skipped.Team = Team.Blue;
        Add(11, 1, 1, type: InteractionType.WalkMagicTile);
        if (kind == InteractionType.Banzaitele) InteractionItem(12, 3, 2, kind);
        var events = ExecutorWalkEvents(); var actor = ExecutorActor(0, 1);
        _client.GetHabbo().Effects.Init(_client.GetHabbo()); actor.SuperFastWalking = true;
        actor.MoveTo(3, 1); ExecutorTick();
        Assert.Equal(3, actor.Movement.PendingCount); Assert.Equal("3,1,0", actor.Statusses["mv"]);
        ExecutorTick();
        Assert.Equal((3, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(new[] { (Plus.HabboHotel.Items.Wired.WiredBoxType.TriggerWalkOffFurni, source.Id),
            (Plus.HabboHotel.Items.Wired.WiredBoxType.TriggerWalkOnFurni, target.Id) },
            events.Select(e => (e.Kind, e.Item)).ToArray());
        Assert.Equal(Team.None, actor.Team); Assert.Equal(0, skipped.InteractingUser);
        Assert.Equal("0", skipped.LegacyDataString); Assert.False(actor.HasStatus("mv"));
    }

    [Fact]
    public void ExecutorFullBanzaiTeamClosesTheGateAndDepartureReopensIt()
    {
        var gate = AcceptanceGate(InteractionType.Banzaigateblue);
        var actor = ExecutorActor(0, 1);
        var teams = _room.GetTeamManagerForBanzai();
        var members = Enumerable.Range(8, 5).Select(AcceptanceTeamMember).ToArray();
        foreach (var member in members) teams.AddUser(member);
        Assert.Equal(5, teams.BlueTeam.Count); Assert.Equal("5", gate.LegacyDataString);
        actor.MoveTo(1, 1); ExecutorTick();
        var navigation = _room.GetGameMap().Navigation!;
        Assert.True((navigation.Grid.Flags[5] & NavFlags.FloorLocked) != 0);
        Assert.Equal((0, 1), (actor.X, actor.Y)); Assert.False(actor.HasStatus("mv"));
        teams.OnUserLeave(members[^1]);
        Assert.Equal(4, teams.BlueTeam.Count); Assert.Equal("4", gate.LegacyDataString);
        actor.MoveTo(1, 1); ExecutorTick();
        Assert.False((navigation.Grid.Flags[5] & NavFlags.FloorLocked) != 0);
        Assert.Equal("1,1,0", actor.Statusses["mv"]);
    }

    [Fact]
    public void ExecutorFreezeGameManagerLocksAndUnlocksRegisteredTeamGates()
    {
        var gate = AcceptanceGate(InteractionType.FreezeBlueGate);
        _room.GetGameManager().AddFurnitureToTeam(gate, Team.Blue);
        var actor = ExecutorActor(0, 1); var freeze = _room.GetFreeze();
        freeze.StartGame(); Assert.True(freeze.GameIsStarted);
        actor.MoveTo(1, 1); ExecutorTick();
        var navigation = _room.GetGameMap().Navigation!;
        Assert.True((navigation.Grid.Flags[5] & NavFlags.FloorLocked) != 0);
        Assert.Equal((0, 1), (actor.X, actor.Y)); Assert.False(actor.HasStatus("mv"));
        freeze.StopGame(userTriggered: true); Assert.False(freeze.GameIsStarted);
        actor.MoveTo(1, 1); ExecutorTick();
        Assert.False((navigation.Grid.Flags[5] & NavFlags.FloorLocked) != 0);
        Assert.Equal("1,1,0", actor.Statusses["mv"]);
    }

    [Fact]
    public void ExecutorRollerClaimBlocksStressIgnoreUsersUntilTheNextTick()
    {
        ExecutorRoller(10, 0, 1);
        var carried = ExecutorRollerActor(0, 1, .5);
        var stress = AcceptanceBot(2, 1, 2, temporary: true); ExecutorTick();
        stress.MoveTo(1, 1); EnableExecutorRollers(); ExecutorTick();
        Assert.Equal((1, 1), (carried.X, carried.Y));
        Assert.True(stress.Movement.Profile.IgnoreUsers); Assert.False(stress.Movement.Profile.LegacyOverride);
        Assert.Equal((2, 1), (stress.X, stress.Y)); Assert.False(stress.HasStatus("mv"));
        var occupancy = _room.GetGameMap().Navigation!.Executor.Claims.OccupancyAt(5, 0);
        Assert.Equal(TargetOccupancy.Stationary, occupancy);
        stress.MoveTo(1, 1); ExecutorTick();
        Assert.Equal("1,1,0", stress.Statusses["mv"]);
        ExecutorTick();
        Assert.Equal((1, 1), (stress.X, stress.Y));
    }

    private RoomUser AcceptanceBot(int x, int y, int id, bool admit = true, bool temporary = false)
    {
        var actor = new RoomUser(0, RoomId, id, _room)
        { X = x, Y = y, InternalRoomId = id, BotData = ProfileBot(temporary), AllowOverride = temporary };
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        Assert.True(users.TryAdd(id, actor));
        if (admit) _room.GetGameMap().Navigation!.Admit(actor);
        return actor;
    }

    private Item AcceptanceGate(InteractionType kind)
    {
        var gate = InteractionItem(10, 1, 1, kind);
        gate.Definition.Walkable = true; gate.Team = Team.Blue;
        return gate;
    }

    private RoomUser AcceptanceTeamMember(int id)
    {
        var client = new TestClient();
        client.SetHabbo(new Habbo { Id = id, Username = $"member{id}", CurrentRoom = _room, Client = client,
            Effects = new EffectsComponent(new FixedTimeProvider(FixedTimeProvider.Epoch)), Access = Plus.HabboHotel.Permissions.UserAccess.Empty,
            HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0) });
        var actor = new RoomUser(id, RoomId, id, _room) { X = 3, Y = 3, Team = Team.Blue, InternalRoomId = id };
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(actor, client);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        Assert.True(users.TryAdd(id, actor)); _room.GetGameMap().Navigation!.Admit(actor);
        return actor;
    }
}
