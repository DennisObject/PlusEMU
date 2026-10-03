using System.Reflection;
using Plus.Core.Settings;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Interactor;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    private static readonly InteractionType[] ApproachKinds =
        [InteractionType.VendingMachine, InteractionType.Teleport, InteractionType.Hopper];

    private static int KindOf(InteractionType kind) => kind switch
    {
        InteractionType.VendingMachine => ApproachActionKind.VendingMachine,
        InteractionType.Teleport => ApproachActionKind.Teleporter,
        _ => ApproachActionKind.Hopper
    };

    private RoomUser ApproachActor(int x, int y, bool autoInteract = true)
    {
        var map = _room.GetGameMap();
        var navigation = new RoomNavigation(_room, map.StaticModel,
            new() { Engine = PathfindingEngine.V2, ApproachAutoInteract = autoInteract });
        typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(map, navigation);
        foreach (var item in _room.GetRoomItemHandler().GetFloor) navigation.Inputs.Attach(item);
        var actor = Viewer(x, y); actor.InternalRoomId = actor.VirtualId; actor.UserId = 7;
        navigation.Admit(actor);
        ExecutorTick();
        return actor;
    }

    private ApproachIntentRegistry Approaches => _room.GetGameMap().Navigation!.Executor.Context.Approaches;

    private ApproachDescriptor Descriptor(Item item, int kind)
    {
        var navigation = _room.GetGameMap().Navigation!;
        var tile = navigation.Grid.Tile(item.SquareInFront.X, item.SquareInFront.Y);
        return new(item.Id, navigation.Inputs.Read(item.Id)!.Version, navigation.Grid.Reference(tile), kind, item.StateGeneration);
    }

    private void WalkToLanding(RoomUser actor, int ticks) { for (var i = 0; i < ticks; i++) ExecutorTick(); }

    [Fact]
    public void ApproachSettingDefaultsOnWhenLoadedAndZeroDisablesIt()
    {
        Assert.True(PathfindingSettings.Load(new ApproachSettings()).ApproachAutoInteract);
        Assert.True(PathfindingSettings.Load(new ApproachSettings("1")).ApproachAutoInteract);
        Assert.False(PathfindingSettings.Load(new ApproachSettings("0")).ApproachAutoInteract);
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void DistantClickStartsTheInteractionOnceOnTheFinalLandingTick(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        WalkToLanding(actor, 2);
        Assert.Equal(0, item.InteractingUser); Assert.Equal((2, 0), (actor.X, actor.Y));
        ExecutorTick();
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.Equal(7, item.InteractingUser);
        Assert.Null(Approaches.Peek(actor));
        if (kind == InteractionType.VendingMachine)
        { Assert.Equal("1", item.LegacyDataString); Assert.False(actor.CanWalk); Assert.Equal(2, item.UpdateCounter); }
        else { Assert.Equal(2, actor.TeleDelay); Assert.True(actor.CanWalk); }
        ExecutorTick();
        Assert.Equal(item.SquareInFront, actor.Coordinate);
    }

    [Fact]
    public void ArrivalStartedVendingDispensesOnTheSecondItemCycleLikeANearClick()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        WalkToLanding(actor, 3);
        Assert.Equal(2, item.UpdateCounter);
        ExecutorTick(); Assert.Equal("1", item.LegacyDataString); Assert.Equal(0, actor.CarryItemId);
        ExecutorTick();
        Assert.Equal("0", item.LegacyDataString); Assert.Equal(0, item.InteractingUser);
        Assert.True(actor.CanWalk); Assert.Equal(DrinkId, actor.CarryItemId);
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void DisabledSettingReproducesTheDistantClickCharacterizationExactly(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ApproachActor(3, 0, autoInteract: false);
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Null(_room.GetGameMap().Navigation!.Executor.Context.Approaches.Peek(actor));
        Assert.Null(actor.Movement.Commands.Read()!.Approach);
        WalkToLanding(actor, 3);
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        for (var i = 0; i < 4; i++) ExecutorTick();
        Assert.Equal(0, item.InteractingUser); Assert.Equal("0", item.LegacyDataString);
        Assert.True(actor.CanWalk); Assert.Equal(0, actor.CarryItemId);
        Assert.Equal(MoveOrigin.User, actor.Movement.Origin);
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void NearClickTimingIsUnchangedWhileAutomaticApproachIsEnabled(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ApproachActor(1, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal(7, item.InteractingUser);
        Assert.Null(actor.Movement.Commands.Read());
        if (kind == InteractionType.VendingMachine)
        {
            Assert.Equal(2, item.UpdateCounter); Assert.False(actor.CanWalk);
            ExecutorTick(); Assert.Equal("1", item.LegacyDataString);
            ExecutorTick(); Assert.Equal("0", item.LegacyDataString); Assert.Equal(DrinkId, actor.CarryItemId);
        }
        else
        {
            Assert.Equal(2, actor.TeleDelay);
            ExecutorTick(); Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
            ExecutorTick(); Assert.Equal((1, 1), (actor.X, actor.Y));
        }
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void ArrivalStartedTimedEntryMovesIntoTheItemOnTheNextTwoTicks(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        WalkToLanding(actor, 3);
        ExecutorTick();
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
        Assert.Equal(MoveOrigin.Interaction, actor.Movement.Origin);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y)); Assert.False(actor.CanWalk);
    }

    [Fact]
    public void IntentIsBoundOnIntakeToTheLifetimeAndCommandSequenceNotByQueueing()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        var command = actor.Movement.Commands.Read()!;
        Assert.Equal(KindOf(InteractionType.VendingMachine), command.Approach!.ActionKind);
        Assert.Equal(item.Id, command.Approach.ItemId);
        Assert.Null(Approaches.Peek(actor));
        ExecutorTick();
        var intent = Approaches.Peek(actor)!;
        Assert.Equal((actor.Movement.LifetimeId, command.Sequence), (intent.LifetimeId, intent.Sequence));
        Assert.Same(command.Approach, intent.Descriptor);
    }

    [Fact]
    public void TemporaryBlockThenReplanToTheSameApproachStillFiresExactlyOnce()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        var bound = Approaches.Peek(actor)!;
        var goalRevision = actor.Movement.GoalRevision;
        Add(12, 3, 1, height: 4, stackable: false);
        ExecutorTick();
        Assert.True(actor.Movement.GoalRevision > goalRevision);
        Assert.Same(bound, Approaches.Peek(actor));
        for (var i = 0; i < 6 && item.InteractingUser == 0; i++) ExecutorTick();
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.Equal(7, item.InteractingUser); Assert.Equal("1", item.LegacyDataString);
        Assert.Null(Approaches.Peek(actor));
    }

    [Fact]
    public void ReplacementCommandPublishedBeforeTheLandingTickSuppressesTheOldIntent()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        WalkToLanding(actor, 2);
        actor.MoveTo(3, 3);
        ExecutorTick();
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.Equal(0, item.InteractingUser); Assert.Equal("0", item.LegacyDataString); Assert.True(actor.CanWalk);
        Assert.Null(Approaches.Peek(actor));
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void ZeroStepAlreadyThereCompletesWithoutFabricatingWalkHooks(InteractionType kind)
    {
        var floor = ExecutorFloor(20, 1, 0);
        var item = ApproachFixture(kind);
        var events = ExecutorWalkEvents();
        var actor = ApproachActor(1, 0);
        var revision = actor.Movement.LocationRevision;
        actor.ApproachItem(item, KindOf(kind));
        ExecutorTick();
        Assert.Equal(7, item.InteractingUser);
        Assert.Empty(events); Assert.Equal(revision, actor.Movement.LocationRevision);
        Assert.False(actor.HasStatus("mv")); Assert.Null(Approaches.Peek(actor));
        Assert.NotNull(floor);
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void RugPlacedOnTheApproachTileJustBeforeTheClickStillStartsOnArrival(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ApproachActor(3, 0);
        ExecutorFloor(20, 1, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        WalkToLanding(actor, 4);
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.Equal(7, item.InteractingUser);
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void StateChangeBetweenPublishAndIntakeCancelsTheApproach(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        item.UpdateState(false, true);
        WalkToLanding(actor, 3);
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.Equal(0, item.InteractingUser); Assert.Equal("0", item.LegacyDataString);
        Assert.True(actor.CanWalk); Assert.Null(Approaches.Peek(actor));
    }

    [Fact]
    public void SamePositionMoveThatQuietlyResetsTheStateCancelsAQueuedApproach()
    {
        var item = ApproachFixture(InteractionType.Teleport);
        var actor = ApproachActor(3, 0);
        item.LegacyDataString = "1";
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(_client, item, item.GetX, item.GetY, item.Rotation, false, false, true));
        Assert.Equal("0", item.LegacyDataString);
        for (var i = 0; i < 8; i++) { ExecutorTick(); Assert.Equal(0, item.InteractingUser); }
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.True(actor.CanWalk); Assert.Null(Approaches.Peek(actor));
    }

    [Fact]
    public void BareSettingsRecordDefaultsApproachAutoInteractOn()
        => Assert.True(new PathfindingSettings().ApproachAutoInteract);

    [Fact]
    public void NewerCommandConsumedWithoutAnApproachReplacesTheBoundIntent()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        Assert.NotNull(Approaches.Peek(actor));
        actor.MoveTo(3, 3);
        ExecutorTick();
        Assert.Null(Approaches.Peek(actor));
        for (var i = 0; i < 4; i++) ExecutorTick();
        Assert.Equal(0, item.InteractingUser);
    }

    [Fact]
    public void LandingHookThatTeleportsTheActorDoesNotFire()
    {
        ExecutorFloor(20, 1, 0);
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        ExecutorObserveLanding((user, landed) =>
        { if (landed.Id == 20) user.SetPos(3, 3, 0); });
        item.Interactor.OnTrigger(_client, item, 0, true);
        for (var i = 0; i < 5; i++) ExecutorTick();
        Assert.Equal(0, item.InteractingUser); Assert.Equal("0", item.LegacyDataString); Assert.True(actor.CanWalk);
        Assert.Null(Approaches.Peek(actor));
    }

    [Fact]
    public void UnreachableApproachFiresNothingAndLeavesNoRegisteredIntent()
    {
        foreach (var (id, x, y) in new[] { (30u, 0, 0), (31u, 0, 1), (32u, 2, 0), (33u, 2, 1) })
            Add(id, x, y, height: 4, stackable: false);
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        for (var i = 0; i < 6; i++) ExecutorTick();
        Assert.Equal((3, 0), (actor.X, actor.Y));
        Assert.Equal(0, item.InteractingUser); Assert.True(actor.CanWalk);
        Assert.Null(Approaches.Peek(actor));
    }

    [Fact]
    public void RemovingTheItemWhileApproachingCancelsTheIntent()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        Assert.NotNull(Approaches.Peek(actor));
        _room.GetRoomItemHandler().RemoveFurniture(_client, item.Id);
        Assert.Null(Approaches.Peek(actor));
        for (var i = 0; i < 4; i++) ExecutorTick();
        Assert.Equal(0, item.InteractingUser); Assert.True(actor.CanWalk);
    }

    [Fact]
    public void MovingTheItemWhileApproachingCancelsTheIntent()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(item, 2, 2, 0));
        Assert.Null(Approaches.Peek(actor));
        for (var i = 0; i < 4; i++) ExecutorTick();
        Assert.Equal(0, item.InteractingUser);
    }

    [Fact]
    public void ItemStateChangeWhileApproachingCancelsTheIntent()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        item.UpdateState(false, true);
        Assert.Null(Approaches.Peek(actor));
        for (var i = 0; i < 4; i++) ExecutorTick();
        Assert.Equal(0, item.InteractingUser); Assert.True(actor.CanWalk);
    }

    [Fact]
    public void ItemBecomingBusyWithoutAStateBroadcastLeavesTheOccupantUntouched()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        item.LegacyDataString = "1"; item.InteractingUser = 99;
        for (var i = 0; i < 4; i++) ExecutorTick();
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.Equal(99, item.InteractingUser); Assert.Equal("1", item.LegacyDataString);
        Assert.True(actor.CanWalk); Assert.Null(Approaches.Peek(actor));
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void BusyInteractorDeclinesAnApproachStartWithoutChangingAnyOwnedState(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ApproachActor(1, 0);
        item.InteractingUser = 99; item.LegacyDataString = kind == InteractionType.VendingMachine ? "1" : "0";
        var interactor = Assert.IsAssignableFrom<IApproachInteractor>(item.Interactor);
        Assert.False(interactor.StartFromApproach(item, actor));
        Assert.Equal(99, item.InteractingUser); Assert.True(actor.CanWalk); Assert.NotEqual(2, actor.TeleDelay);
        Assert.Equal(KindOf(kind), interactor.ActionKind);
    }

    [Fact]
    public void CancelAndActorRemovalReleaseTheIntentIdempotently()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        actor.ClearMovement(true);
        ExecutorTick();
        Assert.Null(Approaches.Peek(actor));
        actor.ClearMovement(true); ExecutorTick();
        Approaches.Cancel(actor); Approaches.Cancel(actor);
        for (var i = 0; i < 4; i++) ExecutorTick();
        Assert.Equal(0, item.InteractingUser);

        var second = ApproachFixture(InteractionType.VendingMachine);
        actor.ApproachItem(second, ApproachActionKind.VendingMachine);
        ExecutorTick();
        _room.GetGameMap().Navigation!.Remove(actor);
        ExecutorTick();
        Assert.Null(Approaches.Peek(actor)); Assert.Equal(0, second.InteractingUser);
    }

    [Fact]
    public void RoomShutdownClearsEveryRegisteredIntent()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        Assert.NotNull(Approaches.Peek(actor));
        _room.GetGameMap().Navigation!.Shutdown();
        Assert.Null(Approaches.Peek(actor));
    }

    [Fact]
    public void LosingTheRoomMidWalkRemovesPermissionAndDoesNotFire()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        _client.GetHabbo().CurrentRoom = null;
        for (var i = 0; i < 4; i++) ExecutorTick();
        Assert.Equal(0, item.InteractingUser); Assert.Equal("0", item.LegacyDataString);
    }

    [Fact]
    public void ReentrantStartCallbackCannotFireTheSameIntentTwice()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(1, 0);
        var starts = 0;
        ApproachCompletion? completion = null;
        completion = ApproachCompletionFor(_ => new CountingInteractor((it, user) =>
        {
            starts++;
            Assert.False(completion!.Complete(user, user.Movement.LocationRevision));
            Assert.Null(Approaches.Consume(user));
            return true;
        }));
        BindIntent(actor, item);
        Assert.True(completion.Complete(actor, actor.Movement.LocationRevision));
        Assert.Equal(1, starts);
        Assert.False(completion.Complete(actor, actor.Movement.LocationRevision));
    }

    public static TheoryData<string> FailedRechecks => new()
    {
        "inactive", "location", "surface", "recordVersion", "removed", "permission", "newerCommand", "kind"
    };

    [Theory]
    [MemberData(nameof(FailedRechecks))]
    public void CompletionConsumesFirstThenAnyFailedRecheckDropsTheIntentWithoutStarting(string failure)
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(1, 0);
        var starts = 0;
        var completion = ApproachCompletionFor(_ => new CountingInteractor((_, _) => { starts++; return true; }));
        BindIntent(actor, item, DescriptorEdit(failure));
        var revision = actor.Movement.LocationRevision;
        BreakRecheck(failure, item, actor, ref revision);
        Assert.False(completion.Complete(actor, revision));
        Assert.Equal(0, starts); Assert.Null(Approaches.Peek(actor));
    }

    [Fact]
    public void PassingRechecksInvokeTheInteractorsStartEntryExactlyOnce()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(1, 0);
        var starts = 0;
        var completion = ApproachCompletionFor(_ => new CountingInteractor((_, _) => { starts++; return true; }));
        BindIntent(actor, item);
        Assert.True(completion.Complete(actor, actor.Movement.LocationRevision));
        Assert.Equal(1, starts); Assert.Null(Approaches.Peek(actor));
    }

    [Fact]
    public void RegistryRebindsReplacesAndCancelsPerActorAndPerItem()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ApproachActor(1, 0);
        var descriptor = Descriptor(item, ApproachActionKind.VendingMachine);
        var registry = new ApproachIntentRegistry();
        registry.Bind(actor, new MoveCommand(5, 1, 0, MoveOrigin.User, Approach: descriptor));
        var first = registry.Peek(actor)!;
        registry.Bind(actor, new MoveCommand(5, 1, 0, MoveOrigin.User, Approach: descriptor));
        Assert.Equal((first.LifetimeId, first.Sequence), (registry.Peek(actor)!.LifetimeId, registry.Peek(actor)!.Sequence));
        registry.Bind(actor, new MoveCommand(6, 1, 0, MoveOrigin.User, Approach: descriptor));
        Assert.Equal(6, registry.Peek(actor)!.Sequence);
        registry.CancelItem(descriptor.ItemId + 1); Assert.NotNull(registry.Peek(actor));
        registry.CancelItem(descriptor.ItemId); Assert.Null(registry.Peek(actor));
        registry.CancelItem(descriptor.ItemId);
        registry.Bind(actor, new MoveCommand(7, 1, 0, MoveOrigin.User, Approach: descriptor));
        registry.Bind(actor, new MoveCommand(8, 1, 0, MoveOrigin.User));
        Assert.Null(registry.Peek(actor));
        registry.Bind(actor, new MoveCommand(9, 1, 0, MoveOrigin.User, Approach: descriptor));
        Assert.NotNull(registry.Consume(actor)); Assert.Null(registry.Consume(actor));
        registry.Bind(actor, new MoveCommand(10, 1, 0, MoveOrigin.User, Approach: descriptor));
        registry.Clear(); Assert.Null(registry.Peek(actor));
    }

    private ApproachCompletion ApproachCompletionFor(Func<Item, IApproachInteractor?> resolve)
        => new(_room, _room.GetGameMap().Navigation!, Approaches, resolve);

    private void BindIntent(RoomUser actor, Item item, Func<ApproachDescriptor, ApproachDescriptor>? edit = null)
    {
        var descriptor = Descriptor(item, ApproachActionKind.VendingMachine);
        var command = new MoveCommand(actor.Movement.NextSequence(), 1, 0, MoveOrigin.User, Approach: edit?.Invoke(descriptor) ?? descriptor);
        actor.Movement.Commands.Publish(command);
        actor.Movement.ConsumedSequence = command.Sequence;
        Approaches.Bind(actor, command);
    }

    private static Func<ApproachDescriptor, ApproachDescriptor>? DescriptorEdit(string failure) => failure switch
    {
        "recordVersion" => d => d with { ItemRecordVersion = d.ItemRecordVersion - 1 },
        "kind" => d => d with { ActionKind = ApproachActionKind.Hopper },
        _ => null
    };

    private void BreakRecheck(string failure, Item item, RoomUser actor, ref long revision)
    {
        switch (failure)
        {
            case "inactive": actor.Movement.State = NavState.Removing; break;
            case "location": revision--; break;
            case "surface": actor.Movement.CurrentRef = null; break;
            case "removed": _room.GetRoomItemHandler().RemoveFurniture(_client, item.Id); break;
            case "permission": _client.GetHabbo().CurrentRoom = null; break;
            case "newerCommand":
                actor.Movement.Commands.Publish(new(actor.Movement.NextSequence(), 3, 3, MoveOrigin.User)); break;
        }
    }

    private sealed class CountingInteractor(Func<Item, RoomUser, bool> start) : IApproachInteractor
    {
        public int ActionKind => ApproachActionKind.VendingMachine;
        public bool StartFromApproach(Item item, RoomUser user) => start(item, user);
    }

    private sealed class ApproachSettings(string? value = null) : ISettingsManager
    {
        public string TryGetValue(string key) => "0";
        public string? GetOptionalValue(string key) => key == "pathfinding.approach_auto_interact" ? value : null;
        public Task Reload() => Task.CompletedTask;
    }
}
