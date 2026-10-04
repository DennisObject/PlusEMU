using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// V2 floor locks follow the legacy lock lifetime; placement validation and commit share one lock;
// multi-tile cargo validates and reserves its whole destination footprint.
public partial class PlacedFurniRoomTests
{
    // V2 follows the legacy lock lifetime: the cell rebuild releases the floor-status override too.
    [Fact]
    public void V2SolidCargoDepartureErasesTheLockLikeLegacy()
    {
        var tail = PlannerLockedSolidCargoChain(PathfindingEngine.V2);
        ExecutorTick();
        Assert.Equal((1, 1, .5), (tail.X, tail.Y, tail.Z));
    }

    [Fact]
    public void RegeneratedV2MapReleasesALockTheTeamNeverUnlocked()
    {
        PlannerRoller(10, 0, 1, 2);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var actor = PlannerActor(1, 0, 1, .5);
        _room.GetGameMap().SetFloorStatus(1, 1, 0);
        _room.GetGameMap().GenerateMaps();
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingOrRemovingTheLockedV2ItemReleasesItsOldTile(bool remove)
    {
        PlannerRoller(10, 0, 1, 2);
        var gate = remove ? PlannerTemporaryWalkable(1, 1) : ExecutorFloor(20, 1, 1);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var actor = PlannerActor(1, 0, 1, .5);
        _room.GetGameMap().SetFloorStatus(1, 1, 0);
        var handler = _room.GetRoomItemHandler();
        Assert.True(remove ? handler.RemoveTemporaryFloorItem(gate) : handler.SetFloorItem(gate, 3, 3, 0));
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
    }

    [Theory]
    [InlineData(PathfindingEngine.V2)]
    public void FurnitureArrivingOnTopOfALockedTileRebuildsItsCell(PathfindingEngine engine)
    {
        PlannerRoller(10, 0, 1, 2);
        InstallRollerChainEngine(engine);
        var actor = PlannerActor(1, 0, 1, .5);
        _room.GetGameMap().SetFloorStatus(1, 1, 0);
        ExecutorFloor(20, 1, 1);
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
    }

    [Fact]
    public void RollerCapabilityRefreshNeverRunsUnderThePlacementLock()
    {
        var underLock = new List<bool>();
        PlannerObserveMembership(() => underLock.Add(Monitor.IsEntered(_room.GetGameMap().PlacementSync)));
        PlannerRoller(10, 0, 1, 2);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var actor = PlannerActor(1, 0, 1, .5);
        StartPlannerRollers(); underLock.Clear(); ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.NotEmpty(underLock);
        Assert.DoesNotContain(true, underLock);
    }

    [Theory]
    [InlineData(PathfindingEngine.V2)]
    public void EarlierHookRotatingAHelperOntoAHigherFloorRejectsItsStaleGroup(PathfindingEngine engine)
    {
        Set("_gamemap", new Gamemap(_room, new RoomModel("roller-floor", 0, 0, 0, 0, "0000\r0002\r0000\r0000", false, 0, false)));
        _room.GetGameMap().GenerateMaps();
        PlannerRoller(5, 0, 3, 2); ExecutorFloor(40, 1, 3);
        PlannerRoller(10, 1, 1, 2);
        var helper = Add(20, 1, 1, z: .5, type: InteractionType.WalkMagicTile, length: 2);
        InstallRollerChainEngine(engine);
        var rider = PlannerActor(1, 0, 3, .5);
        var fired = false;
        PlannerObserveWalkOn(() =>
        {
            if (fired) return;
            fired = true;
            Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, helper, 1, 1, 2, false, false, false, height: helper.GetZ));
        });
        StartPlannerRollers(); ExecutorTick();
        Assert.True(fired);
        Assert.Equal((1, 3), (rider.X, rider.Y));
        Assert.Equal((1, 1, .5, 2), (helper.GetX, helper.GetY, helper.GetZ, helper.Rotation));
        Assert.Equal(1, PlannerSlides());
    }

    // A guild gate away from the rollers makes every capability refresh consult group membership.
    private void PlannerObserveMembership(Action observe)
    {
        var group = new Group(23, "gate", "", "", RoomId, 7, 0, 0, 1, 1, 0, false);
        var groups = Proxy<IGroupManager>((method, args) =>
        {
            observe(); args[1] = group; return (int)args[0]! == group.Id;
        });
        var previous = ((TestProxy)_gameField.GetValue(null)!).Call;
        _gameField.SetValue(null, Proxy<IGame>((method, args) =>
            method == "get_GroupManager" ? groups : previous(method, args)));
        var gate = Add(40, 3, 3, type: InteractionType.GuildGate);
        InitializeNativeState(gate); gate.GroupId = group.Id;
    }

    [Fact]
    public void InPlaceSetterValidatesTheFootprintItCommitsUnderOnePlacementLock()
    {
        var helper = Add(20, 0, 2, type: InteractionType.WalkMagicTile, length: 2);
        var handler = _room.GetRoomItemHandler();
        var moved = true;
        PlannerInterleaveRotation(() => moved = handler.SetFloorItem(helper, 3, 1, 0),
            () => Assert.True(handler.SetFloorItem(null!, helper, 0, 2, 2, false, false, false)));
        Assert.False(moved);
        Assert.Equal((0, 2, 2), (helper.GetX, helper.GetY, helper.Rotation));
    }

    [Theory]
    [InlineData(PathfindingEngine.V2)]
    public void RollerGroupValidatesTheFootprintItCommitsUnderOnePlacementLock(PathfindingEngine engine)
    {
        PlannerRoller(10, 2, 1, 2);
        var helper = Add(20, 2, 1, z: .5, type: InteractionType.WalkMagicTile, length: 2);
        InstallRollerChainEngine(engine);
        PlannerActor(1, 0, 3, 0);
        StartPlannerRollers();
        _client.Packets.Clear(); _client.Sent.Clear();
        PlannerInterleaveRotation(_room.ProcessRoom,
            () => Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, helper, 2, 1, 2, false, false, false)));
        Assert.Equal((2, 1, 2), (helper.GetX, helper.GetY, helper.Rotation));
        Assert.Equal(0, PlannerSlides());
    }

    [Fact]
    public void ClaimOnASecondaryFootprintTileHoldsMultiTileCargo()
    {
        PlannerRoller(10, 0, 1, 2);
        var cargo = PlannerLongCargo(20, 0, 1, walkable: false);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var observer = PlannerActor(1, 3, 3, 0);
        StartPlannerRollers();
        var navigation = _room.GetGameMap().Navigation!;
        Assert.True(navigation.Executor.Claims.TryClaim(observer, navigation.Grid.Tile(1, 2), ClaimKind.Exclusive, TargetOccupancy.None));
        ExecutorTick();
        Assert.Equal((0, 1), (cargo.GetX, cargo.GetY));
        Assert.Equal(0, PlannerSlides());
    }

    [Fact]
    public void WalkerCannotEnterASecondaryTileOfCargoThatJustRolled()
    {
        PlannerRoller(10, 0, 1, 2);
        var cargo = PlannerLongCargo(20, 0, 1, walkable: true);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var walker = PlannerActor(1, 2, 2, 0);
        StartPlannerRollers();
        walker.MoveTo(1, 2); ExecutorTick();
        Assert.Equal((1, 1), (cargo.GetX, cargo.GetY));
        Assert.Equal((2, 2, 0d), (walker.X, walker.Y, walker.Z));
        Assert.DoesNotContain("/mv 1,2", ExecutorUpdate(walker).Status);
        Assert.Equal(TargetOccupancy.None, PlannerOccupancy(1, 2));
    }

    private RoomUser PlannerLockedSolidCargoChain(PathfindingEngine engine)
    {
        PrepareRollerChain(8, false);
        PlannerCargo(300, 1, 1);
        InstallRollerChainEngine(engine);
        var tail = PlannerActor(1, 0, 1, .5);
        _room.GetGameMap().SetFloorStatus(1, 1, 0);
        StartPlannerRollers();
        return tail;
    }

    // Holds the placement lock while `work` runs on another thread, applies `rotate` once that thread
    // is blocked on the lock, then releases it. Whatever `work` validated before blocking is now stale.
    private void PlannerInterleaveRotation(Action work, Action rotate)
    {
        Exception? failure = null;
        var worker = new Thread(() => { try { work(); } catch (Exception error) { failure = error; } });
        lock (_room.GetGameMap().PlacementSync)
        {
            worker.Start();
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while ((worker.ThreadState & ThreadState.WaitSleepJoin) == 0 && worker.IsAlive && DateTime.UtcNow < deadline)
                Thread.Sleep(1);
            Assert.True(worker.IsAlive, "worker finished without waiting for the placement lock");
            rotate();
        }
        Assert.True(worker.Join(TimeSpan.FromSeconds(30)));
        if (failure != null) throw failure;
    }

    private Item PlannerLongCargo(uint id, int x, int y, bool walkable)
    {
        var item = Furni(id, InteractionType.None, WiredBoxType.None);
        item.Definition.Walkable = walkable; item.Definition.Height = .25;
        item.Definition.Width = 1; item.Definition.Length = 2;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, x, y, 0, true, false, false, height: .5));
        return item;
    }

    private Item PlannerTemporaryWalkable(int x, int y)
    {
        var definition = Furni(0, InteractionType.None, WiredBoxType.None).Definition;
        definition.Walkable = true; definition.Height = 0; definition.Width = definition.Length = 1;
        var item = _room.GetRoomItemHandler().PlaceTemporaryFloorItem(definition, 7, x, y, 0);
        Assert.NotNull(item);
        return item!;
    }
}
