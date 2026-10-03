using System.Reflection;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// Room (4x4, door 0,0): stair 20 at (1,1) tops out at 1; rug 22 on the floor at (2,1) and a zero-height deck 21 above it at 2.
public partial class PlacedFurniRoomTests
{
    [Fact]
    public void LayeredExecutorWalksUnderTheDeckAndDispatchesOnlyFloorContacts()
    {
        LayeredBridge();
        var events = ExecutorWalkEvents();
        var actor = LayeredActor(2, 0);
        actor.MoveTo(2, 2); ExecutorTick(); ExecutorTick();
        Assert.Equal((2, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(new SurfaceRef(LayeredTile(2, 1), 22, SurfaceKind.Top), actor.Movement.CurrentRef);
        ExecutorTick();
        Assert.Equal((2, 2, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(new[] { (WiredBoxType.TriggerWalkOnFurni, 22u), (WiredBoxType.TriggerWalkOffFurni, 22u) },
            events.Select(e => (e.Kind, e.Item)).ToArray());
    }

    [Fact]
    public void LayeredExecutorCrossesTheDeckOverAFloorOccupantAndDispatchesOnlyDeckContacts()
    {
        LayeredBridge();
        var events = ExecutorWalkEvents();
        var actor = LayeredActor(0, 1);
        ExecutorAdditionalBot(2, 1, 5);
        actor.MoveTo(3, 1); ExecutorTick();
        var heights = new List<(int, double)>();
        for (var tick = 0; tick < 3; tick++) { ExecutorTick(); heights.Add((actor.X, actor.Z)); }
        Assert.Equal(new[] { (1, 1d), (2, 2d), (3, 0d) }, heights);
        Assert.Equal(new[] { (WiredBoxType.TriggerWalkOnFurni, 20u), (WiredBoxType.TriggerWalkOffFurni, 20u),
            (WiredBoxType.TriggerWalkOnFurni, 21u), (WiredBoxType.TriggerWalkOffFurni, 21u) },
            events.Select(e => (e.Kind, e.Item)).ToArray());
    }

    [Fact]
    public void LayeredDeckOccupantDoesNotBlockTheFloorBelow()
    {
        LayeredBridge();
        var actor = LayeredActor(2, 0);
        var bot = ExecutorAdditionalBot(3, 0, 5);
        bot.SetPos(2, 1, 2); ExecutorTick();
        Assert.Equal(2, bot.Z);
        actor.MoveTo(2, 2); ExecutorTick(); ExecutorTick();
        Assert.Equal((2, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(2, LayeredNavigation.Executor.Claims.TileCount[LayeredTile(2, 1)]);
        ExecutorTick();
        Assert.Equal((2, 2), (actor.X, actor.Y));
    }

    [Theory]
    [InlineData(2d, 0d)]
    [InlineData(0d, 2d)]
    public void LayeredOccupiedGoalIsCheckedPerLevel(double occupiedZ, double expectedZ)
    {
        LayeredBridge();
        var actor = LayeredActor(0, 1);
        var bot = ExecutorAdditionalBot(3, 0, 5);
        bot.SetPos(2, 1, occupiedZ); ExecutorTick();
        actor.MoveTo(2, 1);
        for (var tick = 0; tick < 4; tick++) ExecutorTick();
        Assert.Equal((2, 1, expectedZ), (actor.X, actor.Y, actor.Z));
        Assert.Equal(occupiedZ, bot.Z);
    }

    [Fact]
    public void LayeredStressBotGoalIsAcceptedWhenEveryLevelIsOccupied()
    {
        LayeredBridge();
        LayeredActor(0, 3);
        var floor = ExecutorAdditionalBot(2, 1, 5);
        var deck = ExecutorAdditionalBot(3, 0, 6);
        deck.SetPos(2, 1, 2);
        var stress = ExecutorAdditionalBot(2, 3, 7);
        typeof(RoomBot).GetField("<IsTemporary>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(stress.BotData, true);
        ExecutorTick();
        stress.MoveTo(2, 1);
        for (var tick = 0; tick < 4; tick++) ExecutorTick();
        Assert.Equal((2, 1), (stress.X, stress.Y));
        Assert.Equal((0d, 2d), (floor.Z, deck.Z));
    }

    [Fact]
    public void LayeredSharedDoorGoalIsAcceptedWhileOccupied()
    {
        LayeredBridge();
        var actor = LayeredActor(1, 0);
        var bot = ExecutorAdditionalBot(0, 0, 5);
        ExecutorTick();
        actor.MoveTo(0, 0); ExecutorTick(); ExecutorTick();
        Assert.Null(_room.GetRoomUserManager().GetRoomUserByVirtualId(actor.VirtualId));
        Assert.Equal((0, 0), (bot.X, bot.Y));
    }

    [Fact]
    public void LayeredCoalescedRugsBlockAsOneNodeAndFireEachHookOnce()
    {
        LayeredBridge();
        ExecutorFloor(30, 1, 2); ExecutorFloor(31, 1, 2);
        var events = ExecutorWalkEvents();
        var actor = LayeredActor(0, 2);
        var bot = ExecutorAdditionalBot(1, 2, 5);
        actor.MoveTo(2, 2);
        var visited = new List<(int, int)>();
        for (var tick = 0; tick < 4; tick++) { ExecutorTick(); visited.Add((actor.X, actor.Y)); }
        Assert.Equal((2, 2), (actor.X, actor.Y));
        Assert.DoesNotContain((1, 2), visited);
        bot.SetPos(3, 0, 0); ExecutorTick(); events.Clear();
        actor.MoveTo(0, 2);
        for (var tick = 0; tick < 3; tick++) ExecutorTick();
        Assert.Equal((0, 2), (actor.X, actor.Y));
        Assert.Equal(new uint[] { 30, 31 }, events.Where(e => e.Kind == WiredBoxType.TriggerWalkOnFurni).Select(e => e.Item).Order());
        Assert.Equal(new uint[] { 30, 31 }, events.Where(e => e.Kind == WiredBoxType.TriggerWalkOffFurni).Select(e => e.Item).Order());
    }

    [Fact]
    public void LayeredRebindLandsOnTheHighestSurfaceAtOrBelowWhenTheDeckIsRemoved()
    {
        LayeredBridge();
        var actor = LayeredActor(2, 0);
        actor.SetPos(2, 1, 2); ExecutorTick();
        Assert.Equal(new SurfaceRef(LayeredTile(2, 1), 21, SurfaceKind.Top), actor.Movement.CurrentRef);
        _room.GetRoomItemHandler().RemoveFurniture(null!, 21); ExecutorTick();
        Assert.Equal(0, actor.Z);
        Assert.Equal(new SurfaceRef(LayeredTile(2, 1), 22, SurfaceKind.Top), actor.Movement.CurrentRef);
        Assert.Equal((2, 1, "0"), (ExecutorUpdate(actor).X, ExecutorUpdate(actor).Y, ExecutorUpdate(actor).Z));
    }

    [Fact]
    public void LayeredRebindKeepsAFloorActorBelowANewDeckAndFollowsADeckHeightChange()
    {
        ExecutorFloor(22, 2, 1);
        var actor = LayeredActor(2, 1);
        var floor = actor.Movement.CurrentRef;
        var deck = LayeredFloorOnto(21, 2, 1, 2); ExecutorTick();
        Assert.Equal((0d, floor), (actor.Z, actor.Movement.CurrentRef));
        actor.SetPos(2, 1, 2); ExecutorTick();
        var deckSlot = LayeredNavigation.Grid.SlotOf(actor.Movement.CurrentRef!.Value);
        Assert.True(deckSlot >= LayeredNavigation.Grid.TileCount);
        ExecutorFloor(40, 0, 3); ExecutorTick();
        Assert.Same(actor, LayeredNavigation.Executor.Claims.Head[deckSlot]!.Actor);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(deck, 2, 1, 2.5)); ExecutorTick();
        Assert.Equal(2.5, actor.Z);
        Assert.Equal(new SurfaceRef(LayeredTile(2, 1), 21, SurfaceKind.Top), actor.Movement.CurrentRef);
        Assert.Equal("2.5", ExecutorUpdate(actor).Z);
    }

    [Fact]
    public void LayeredPinnedOverflowBeyondFourSurfacesSendsTheLowestActorOffGraph()
    {
        var actor = LayeredActor(3, 2);
        LayeredNavigation.Compiler.SurfacePinned = _ => true;
        for (uint id = 40; id < 44; id++) { LayeredFloorOnto(id, 3, 2, (id - 39) * 2); ExecutorTick(); }
        Assert.Equal(4, LayeredNavigation.Grid.SurfaceCount(LayeredTile(3, 2)));
        Assert.Null(actor.Movement.CurrentRef);
        Assert.Equal((3, 2, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Same(actor, LayeredNavigation.Executor.Claims.OffGraphHead[LayeredTile(3, 2)]!.Actor);
    }

    [Fact]
    public void LayeredForcedPlacementResolvesTheRequestedLevel()
    {
        LayeredBridge();
        var events = ExecutorWalkEvents();
        var actor = LayeredActor(0, 1);
        actor.TeleportEnabled = true;
        actor.MoveTo(2, 1); ExecutorTick();
        Assert.Equal((2, 1, 2d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(new[] { (WiredBoxType.TriggerWalkOnFurni, 21u) }, events.Select(e => (e.Kind, e.Item)).ToArray());
        actor.SetPos(2, 1, 0); ExecutorTick();
        Assert.Equal(new SurfaceRef(LayeredTile(2, 1), 22, SurfaceKind.Top), actor.Movement.CurrentRef);
        actor.SetPos(2, 1, 1); ExecutorTick();
        Assert.Null(actor.Movement.CurrentRef); Assert.Equal(1, actor.Z);
    }

    [Fact]
    public void LayeredExcludedLowerSeatGoalTakesTheStairDetourToTheDeck()
    {
        var seat = ExecutorFloor(50, 2, 2, height: 1); seat.Definition.Walkable = false; seat.Definition.IsSeat = true;
        ExecutorFloor(51, 2, 2, z: 2); ExecutorFloor(52, 3, 1, height: 1);
        var actor = LayeredActor(1, 2);
        var sitter = ExecutorAdditionalBot(3, 3, 5);
        sitter.SetPos(2, 2, 0); ExecutorTick();
        Assert.Equal(SurfaceKind.SeatBase, sitter.Movement.CurrentRef!.Value.Kind);
        sitter.MoveTo(0, 3); ExecutorTick();
        Assert.True(sitter.Movement.HasIntent);
        actor.MoveTo(2, 2);
        var path = new List<(int, int, double)>();
        for (var tick = 0; tick < 5; tick++) { ExecutorTick(); path.Add((actor.X, actor.Y, actor.Z)); }
        Assert.Equal(new[] { (1, 2, 0d), (2, 1, 0d), (3, 1, 1d), (2, 2, 2d), (2, 2, 2d) }, path);
        Assert.Equal(new SurfaceRef(LayeredTile(2, 2), 51, SurfaceKind.Top), actor.Movement.CurrentRef);
    }

    [Theory]
    [InlineData(false, 17)]
    [InlineData(true, 0)]
    public void LayeredEffectTileAppliesOnlyToItsOwnLevel(bool under, int expected)
    {
        var effect = ExecutorFloor(21, 2, 1, z: 2); effect.Definition.InteractionType = InteractionType.Effect;
        effect.Definition.EffectId = 17; InitializeNativeState(effect);
        ExecutorFloor(20, 1, 1, height: 1);
        var actor = LayeredActor(under ? 2 : 0, under ? 0 : 1); InitializeClientEffects();
        if (!under) ExecutorAdditionalBot(2, 1, 5);
        actor.MoveTo(under ? 2 : 3, under ? 2 : 1);
        for (var tick = 0; tick < (under ? 2 : 3); tick++) ExecutorTick();
        Assert.Equal(under ? 0 : 2, actor.Z);
        Assert.Equal(expected, _client.GetHabbo().Effects.CurrentEffect);
    }

    [Theory]
    [InlineData(true, 38)]
    [InlineData(false, 0)]
    public void LayeredFloorEffectFollowsTheAnnouncedLevel(bool under, int expected)
    {
        var skates = ExecutorFloor(22, 2, 1); skates.Definition.InteractionType = InteractionType.IceSkates;
        ExecutorFloor(21, 2, 1, z: 2); ExecutorFloor(20, 1, 1, height: 1);
        var actor = LayeredActor(under ? 2 : 1, under ? 0 : 1); InitializeClientEffects();
        _client.GetHabbo().Gender = "M";
        if (!under) { actor.SetPos(1, 1, 1); ExecutorAdditionalBot(2, 1, 5); ExecutorTick(); }
        actor.MoveTo(2, 1); ExecutorTick();
        Assert.Contains($"/mv 2,1,{(under ? "0" : "2")}/", ExecutorUpdate(actor).Status);
        Assert.Equal(expected, _client.GetHabbo().Effects.CurrentEffect);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void LayeredArrowTileOnlyTriggersOnItsOwnLevel(bool under, bool unlocked)
    {
        var arrow = ExecutorFloor(22, 2, 1); arrow.Definition.InteractionType = InteractionType.Arrow;
        ExecutorFloor(21, 2, 1, z: 2); ExecutorFloor(20, 1, 1, height: 1);
        var actor = LayeredActor(under ? 2 : 0, under ? 0 : 1);
        if (!under) ExecutorAdditionalBot(2, 1, 5);
        actor.CanWalk = false;
        actor.MoveTo(2, 1, MoveOrigin.Wired);
        for (var tick = 0; tick < 4; tick++) ExecutorTick();
        Assert.Equal((2, 1, under ? 0d : 2d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(unlocked, actor.CanWalk);
    }

    [Fact]
    public void LayeredRoomWithGameFurnitureStaysAtOneSurface()
    {
        LayeredBridge();
        var puck = ExecutorFloor(60, 0, 3); puck.Definition.InteractionType = InteractionType.Banzaifloor;
        var actor = LayeredActor(2, 0);
        Assert.False(LayeredNavigation.Grid.Layered);
        actor.MoveTo(2, 2);
        var visited = new List<(int, int)>();
        for (var tick = 0; tick < 4; tick++) { ExecutorTick(); visited.Add((actor.X, actor.Y)); }
        Assert.Equal((2, 2), (actor.X, actor.Y));
        Assert.DoesNotContain((2, 1), visited);
    }

    [Fact]
    public void LayeredNonGoalSurfaceOnTheGoalTileIsCrossedAsTransit()
    {
        // Walkthrough corridor along y=1: the floor under the deck at (2,1) holds a stationary occupant, so
        // only the deck is a goal; the route crosses that floor, climbs the stair and returns onto the deck.
        uint wall = 70;
        foreach (var (x, y) in new[] { (1, 0), (2, 0), (3, 0), (0, 2), (1, 2), (2, 2), (3, 2) })
            ExecutorFloor(wall++, x, y, height: 1).Definition.Walkable = false;
        ExecutorFloor(20, 1, 1, height: 1); ExecutorFloor(21, 2, 1, z: 2);
        _room.RoomBlockingEnabled = true;
        var actor = LayeredActor(3, 1);
        ExecutorAdditionalBot(2, 1, 5);
        actor.MoveTo(2, 1);
        var path = new List<(int, int, double)>();
        for (var tick = 0; tick < 4; tick++) { ExecutorTick(); path.Add((actor.X, actor.Y, actor.Z)); }
        Assert.Equal(new[] { (3, 1, 0d), (2, 1, 0d), (1, 1, 1d), (2, 1, 2d) }, path);
        Assert.Equal(new SurfaceRef(LayeredTile(2, 1), 21, SurfaceKind.Top), actor.Movement.CurrentRef);
    }

    [Theory]
    [InlineData(ClaimKind.Roller, false)]
    [InlineData(ClaimKind.Roller, true)]
    [InlineData(ClaimKind.Exclusive, false)]
    [InlineData(ClaimKind.Exclusive, true)]
    public void LayeredClaimsOnAFreedOrReassignedOverflowSlotAreCleared(ClaimKind kind, bool reassigned)
    {
        LayeredBridge();
        var actor = LayeredActor(0, 3);
        var claims = LayeredNavigation.Executor.Claims;
        var deck = LayeredNavigation.Grid.SlotOf(new SurfaceRef(LayeredTile(2, 1), 21, SurfaceKind.Top));
        Assert.True(deck >= LayeredNavigation.Grid.TileCount);
        Assert.True(claims.TryClaim(actor, deck, kind, TargetOccupancy.None));
        _room.GetRoomItemHandler().RemoveFurniture(null!, 21);
        if (reassigned) ExecutorFloor(23, 3, 2, z: 2);
        using (RoomOwnerScope.Enter(_room)) LayeredNavigation.ApplyDirty();
        if (reassigned) Assert.Equal(deck, LayeredNavigation.Grid.SlotOf(new SurfaceRef(LayeredTile(3, 2), 23, SurfaceKind.Top)));
        Assert.Equal(TargetOccupancy.None, claims.OccupancyAt(deck, 0));
        Assert.True(claims.TryClaim(actor, deck, ClaimKind.Exclusive, (TargetOccupancy)127));
    }

    [Fact]
    public void LayeredDeferredGoalIsReResolvedWhenItsSurfaceSlotIsReused()
    {
        var deck = ExecutorFloor(21, 3, 1, z: 1);
        var actor = LayeredActor(0, 1, new() { Engine = PathfindingEngine.V2, LayeringEnabled = true, MaxExpansionsPerRoomTick = 0 });
        var slot = LayeredNavigation.Grid.SlotOf(new SurfaceRef(LayeredTile(3, 1), 21, SurfaceKind.Top));
        ExecutorAdditionalBot(3, 1, 5); ExecutorTick();
        actor.MoveTo(3, 1); ExecutorTick();
        Assert.True(actor.Movement.HasIntent);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(deck, 2, 1, 1)); ExecutorTick();
        Assert.Equal(slot, LayeredNavigation.Grid.SlotOf(new SurfaceRef(LayeredTile(2, 1), 21, SurfaceKind.Top)));
        typeof(RoomNavigation).GetField("<Settings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(LayeredNavigation, LayeredNavigation.Settings with { MaxExpansionsPerRoomTick = 200000 });
        for (var tick = 0; tick < 4; tick++) ExecutorTick();
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
    }

    [Fact]
    public void LayeredSwitchToOneSurfaceRemapsSurvivingClaimsBySurface()
    {
        LayeredBridge();
        var actor = LayeredActor(0, 1);
        ExecutorAdditionalBot(2, 1, 5);
        actor.SetPos(1, 1, 1); ExecutorTick();
        var deck = new SurfaceRef(LayeredTile(2, 1), 21, SurfaceKind.Top);
        var overflow = LayeredNavigation.Grid.SlotOf(deck);
        actor.MoveTo(2, 1); ExecutorTick();
        Assert.Equal(deck, actor.Movement.Pending[0]);
        var claims = LayeredNavigation.Executor.Claims;
        Assert.Equal(TargetOccupancy.ExclusiveClaim, claims.OccupancyAt(overflow, 0) & TargetOccupancy.ExclusiveClaim);
        ExecutorFloor(60, 3, 3).Definition.InteractionType = InteractionType.Banzaifloor;
        LayeredNavigation.Inputs.Attach(_room.GetRoomItemHandler().GetItem(60));
        using (RoomOwnerScope.Enter(_room)) LayeredNavigation.ApplyDirty();
        Assert.False(LayeredNavigation.Grid.Layered);
        Assert.Equal(1, actor.Movement.PendingCount);
        Assert.Equal(LayeredTile(2, 1), LayeredNavigation.Grid.SlotOf(deck));
        Assert.Equal(TargetOccupancy.None, claims.OccupancyAt(overflow, 0));
        Assert.Equal(TargetOccupancy.ExclusiveClaim, claims.OccupancyAt(LayeredTile(2, 1), 0) & TargetOccupancy.ExclusiveClaim);
    }

    private RoomNavigation LayeredNavigation => _room.GetGameMap().Navigation!;
    private int LayeredTile(int x, int y) => LayeredNavigation.Grid.Tile(x, y);

    private void LayeredBridge()
    {
        ExecutorFloor(20, 1, 1, height: 1);
        ExecutorFloor(22, 2, 1);
        ExecutorFloor(21, 2, 1, z: 2);
    }

    // Legacy placement refuses tiles with users, so furniture arrives above an actor by a direct move.
    private Item LayeredFloorOnto(uint id, int x, int y, double z)
    {
        var item = ExecutorFloor(id, 3, 0, z: z);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(item, x, y, z));
        return item;
    }

    private RoomUser LayeredActor(int x, int y, PathfindingSettings? settings = null)
    {
        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel, settings ?? new() { Engine = PathfindingEngine.V2, LayeringEnabled = true });
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, navigation);
        foreach (var item in _room.GetRoomItemHandler().GetFloor) navigation.Inputs.Attach(item);
        var actor = Viewer(x, y); actor.InternalRoomId = actor.VirtualId; actor.UserId = 7;
        navigation.Admit(actor);
        ExecutorTick();
        return actor;
    }
}
