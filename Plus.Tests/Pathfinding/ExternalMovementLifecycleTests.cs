using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Incoming.Rooms.AI.Pets;
using Plus.Communication.Packets.Incoming.Rooms.AI.Pets.Horse;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.Chat.Pets.Locale;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Pets;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void WiredRelocationDefersPhysicalMembershipGoalAndSlideUntilTheRoomOwner()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1);
        ExecutorTick();
        var revision = actor.Movement.LocationRevision;
        Assert.True(WiredRoomOperations.RelocateAvatar(_room, actor, 2, 2, slide: true));
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal((3, 1), (actor.GoalX, actor.GoalY));
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        Assert.Equal(1, actor.Movement.PendingCount);
        Assert.DoesNotContain(ServerPacketHeader.SlideObjectBundleComposer, _client.Sent);
        ExecutorTick();
        Assert.Equal((2, 2, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(revision + 1, actor.Movement.LocationRevision);
        Assert.Equal((2, 2), (actor.GoalX, actor.GoalY));
        Assert.Equal(0, actor.Movement.PendingCount);
        Assert.False(actor.HasStatus("mv"));
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        Assert.Contains(ServerPacketHeader.SlideObjectBundleComposer, _client.Sent);
    }

    [Fact]
    public void WiredRelocationInsideOwnerUsesForcePlacementAndReleasesTheOldPendingClaim()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1);
        ExecutorTick();
        var revision = actor.Movement.LocationRevision;

        using (RoomOwnerScope.Enter(_room)) {
            Assert.True(WiredRoomOperations.RelocateAvatar(_room, actor, 2, 2, slide: false));
        }

        Assert.Equal((2, 2, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(revision + 1, actor.Movement.LocationRevision);
        Assert.Equal(0, actor.Movement.PendingCount);
        Assert.False(actor.HasStatus("mv"));
        Assert.False(actor.Movement.HasIntent);
        Assert.Equal(TargetOccupancy.None, _room.GetGameMap().Navigation!.Executor.Context.Claims.OccupancyAt(5, 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WiredFreezeCancelsThePendingBatchOrQueuedSearchAtTheOwnerBoundary(bool queuedSearch)
    {
        var actor = ExecutorConfiguredActor(new()
        {
            Engine = PathfindingEngine.V2,
            MaxExpansionsPerRoomTick = queuedSearch ? 0 : 200000
        });
        actor.MoveTo(3, 1);
        ExecutorTick();
        Assert.True(WiredAvatarState.For(_room).FreezeUser(actor, 0, cancelOnTeleport: true));
        Assert.Equal(!queuedSearch, actor.HasStatus("mv"));
        ExecutorTick();
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(0, actor.Movement.PendingCount);
        Assert.False(actor.Movement.HasIntent);
        Assert.True(actor.Frozen);
        Assert.False(actor.CanWalk);
        Assert.False(actor.HasStatus("mv"));
        Assert.Equal(0, DrainRemainingSearches(_room.GetGameMap().Navigation!));
        Assert.Equal(TargetOccupancy.None, _room.GetGameMap().Navigation!.Executor.Context.Claims.OccupancyAt(5, 0));
    }

    [Fact]
    public void RideHorseMountQueuesOneCoherentPlacementAndCancelsBothOldBatches()
    {
        var rider = ExecutorActor(0, 1);
        var horse = ExternalLifecycleHorse(2, 1);
        ExecutorTick();
        rider.MoveTo(3, 1);
        ExecutorTick();
        RideExternalHorse(horse, mount: true);
        Assert.Equal((0, 1, 0d), (rider.X, rider.Y, rider.Z));
        Assert.Equal((2, 1, 0d), (horse.X, horse.Y, horse.Z));
        Assert.False(rider.RidingHorse);
        Assert.False(horse.RidingHorse);
        ExecutorTick();
        Assert.Equal((0, 1, 1d), (rider.X, rider.Y, rider.Z));
        Assert.Equal((0, 1, 0d), (horse.X, horse.Y, horse.Z));
        Assert.True(rider.RidingHorse);
        Assert.True(horse.RidingHorse);
        Assert.Equal(horse.VirtualId, rider.HorseId);
        Assert.Equal(rider.VirtualId, horse.HorseId);
        Assert.Equal(horse.Movement.CurrentRef, rider.Movement.CurrentRef);
        Assert.Equal(0, rider.Movement.PendingCount);
        Assert.Equal(0, horse.Movement.PendingCount);
        Assert.False(rider.HasStatus("mv"));
        Assert.False(horse.HasStatus("mv"));
        ExecutorTick();
        ExecutorTick();
        Assert.Equal((0, 1), (rider.X, rider.Y));
        Assert.Equal((0, 1), (horse.X, horse.Y));
    }

    [Fact]
    public void RideHorseDismountCancelsTheOldGroupBatchBeforeTheLegacyDismountMove()
    {
        var rider = ExecutorActor(0, 1);
        var horse = ExternalLifecycleHorse(0, 1);
        ExecutorTick();
        EstablishExternalMountedGroup(rider, horse);
        rider.MoveTo(2, 1);
        ExecutorTick();
        var oldLanding = _room.GetGameMap().Navigation!.Grid.Reference(5);
        RideExternalHorse(horse, mount: false);
        Assert.True(rider.RidingHorse);
        Assert.True(horse.RidingHorse);
        ExecutorTick();
        Assert.False(rider.RidingHorse);
        Assert.False(horse.RidingHorse);
        Assert.Equal((0, 1), (rider.X, rider.Y));
        Assert.Equal((0, 1), (horse.X, horse.Y));
        Assert.Equal((2, 3), (rider.GoalX, rider.GoalY));
        Assert.DoesNotContain(oldLanding, rider.Movement.Pending.Take(rider.Movement.PendingCount));
        Assert.Equal(0, horse.Movement.PendingCount);
        Assert.False(horse.HasStatus("mv"));
        ExecutorTick();
        Assert.Equal(2, rider.Y);
        Assert.Equal((0, 1), (horse.X, horse.Y));
    }

    [Fact]
    public void PickUpPetCancelsTheMountedGroupBeforeAnyPendingHorseLanding()
    {
        var rider = ExecutorActor(0, 1);
        var horse = ExternalLifecycleHorse(0, 1);
        ExecutorTick();
        EstablishExternalMountedGroup(rider, horse);
        rider.MoveTo(2, 1);
        ExecutorTick();
        var inventory = Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory);
        _client.GetHabbo().Inventory = new InventoryComponent { Furniture = inventory.Furniture, Pets = new([]) };
        var database = Proxy<IDatabase>((method, _) => method == "Connection" ? new PetPickupConnection() : throw new NotSupportedException(method));
        var service = new PetPlacementService(
            new PetRoomStore(database, NullLogger<PetRoomStore>.Instance),
            Proxy<IGameClientManager>((_, _) => _client),
            Proxy<ISettingsManager>((method, _) => throw new NotSupportedException(method)));
        new PickUpPetEvent(service)
            .Parse(_room, _client, ClientPacket(horse.PetData.PetId)).GetAwaiter().GetResult();
        Assert.Equal(NavState.Removing, horse.Movement.State);
        Assert.True(rider.RidingHorse);
        ExecutorTick();
        Assert.Equal((0, 1), (rider.X, rider.Y));
        Assert.False(rider.RidingHorse);
        Assert.Equal(0, horse.Movement.PendingCount);
        Assert.False(horse.Movement.HasIntent);
        Assert.False(horse.HasStatus("mv"));
        Assert.Null(_room.GetRoomUserManager().GetRoomUserByVirtualId(horse.VirtualId));
        Assert.DoesNotContain(horse, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        Assert.Contains(horse.PetData.PetId, Assert.IsType<InventoryComponent>(_client.GetHabbo().Inventory).Pets.Pets.Keys);
    }

    [Fact]
    public void RoomUnloadCleansEveryLifetimeBeforeDestroyingTheMapWithoutAnotherTick()
    {
        var user = ExecutorActor(0, 1);
        var horse = ExternalLifecycleHorse(2, 1);
        ExecutorTick();
        user.MoveTo(1, 1);
        horse.MoveTo(3, 1);
        ExecutorTick();
        var navigation = _room.GetGameMap().Navigation!;
        var userRevision = user.Movement.LocationRevision;
        var petRevision = horse.Movement.LocationRevision;
        InitializeExternalUnloadCollections();
        _room.Dispose();
        Assert.True(_room.MDisposed);
        Assert.Null(_room.GetGameMap());
        Assert.Equal(NavState.Removing, user.Movement.State);
        Assert.Equal(NavState.Removing, horse.Movement.State);
        Assert.True(user.Movement.LocationRevision > userRevision);
        Assert.True(horse.Movement.LocationRevision > petRevision);
        Assert.Equal(0, user.Movement.PendingCount);
        Assert.Equal(0, horse.Movement.PendingCount);
        Assert.False(user.Movement.HasIntent);
        Assert.False(horse.Movement.HasIntent);
        Assert.All(navigation.Executor.Context.Claims.TileCount, count => Assert.Equal(0, count));
        Assert.All(navigation.Executor.Context.Claims.Count, count => Assert.Equal(0, count));
        Assert.Equal(0, DrainRemainingSearches(navigation));
        Assert.Null(_client.GetHabbo().CurrentRoom);
    }

    [Fact]
    public void ManagerDisposalCancelsBotPendingMovementWithoutWaitingForAnUnavailableTick()
    {
        ExecutorActor(0, 1);
        var horse = ExternalLifecycleHorse(2, 1);
        ExecutorTick();
        horse.MoveTo(3, 1);
        ExecutorTick();
        var navigation = _room.GetGameMap().Navigation!;
        var revision = horse.Movement.LocationRevision;
        _room.GetRoomUserManager().Dispose();
        Assert.Equal(NavState.Removing, horse.Movement.State);
        Assert.True(horse.Movement.LocationRevision > revision);
        Assert.Equal(0, horse.Movement.PendingCount);
        Assert.False(horse.Movement.HasIntent);
        Assert.All(navigation.Executor.Context.Claims.TileCount, count => Assert.Equal(0, count));
    }

    [Fact]
    public void LocationRevisionAbortsOldCommitBookkeepingAndLeavesANewerHookCommandForNextTick()
    {
        var landing = ExecutorFloor(10, 1, 1);
        var destination = ExecutorFloor(11, 3, 2, z: .75);
        var actor = ExecutorActor(0, 1);
        long afterPlacement = -1;
        ExecutorObserveLanding((user, item) =>
        {
            if (item != landing) {
                return;
            }

            _room.GetGameMap().TeleportToItem(user, destination);
            afterPlacement = user.Movement.GoalRevision;
            user.MoveTo(3, 1);
        });
        actor.MoveTo(2, 1);
        ExecutorTick();
        ExecutorTick();
        Assert.Equal((3, 2, .75), (actor.X, actor.Y, actor.Z));
        Assert.Equal(afterPlacement, actor.Movement.GoalRevision);
        Assert.False(actor.HasStatus("mv"));
        Assert.False(actor.Movement.HasIntent);
        Assert.True(actor.Movement.Commands.Read()!.Sequence > actor.Movement.ConsumedSequence);
        ExecutorTick();
        Assert.Equal((3, 1), (actor.GoalX, actor.GoalY));
        Assert.Contains("/mv 3,1,0/", ExecutorUpdate(actor).Status);
    }

    private RoomUser ExternalLifecycleHorse(int x, int y)
    {
        var horse = ExecutorAdditionalBot(x, y, 2);
        horse.BotData.AiType = BotAiType.Pet;
        horse.PetData = (Pet)RuntimeHelpers.GetUninitializedObject(typeof(Pet));
        var pet = horse.PetData;
        pet.PetId = 50;
        pet.VirtualId = horse.VirtualId;
        pet.OwnerId = 7;
        pet.RoomId = RoomId;
        pet.Name = "horse";
        pet.OwnerName = "owner";
        pet.Type = 13;
        pet.Race = "0";
        pet.Color = "ffffff";
        pet.GnomeClothing = "";
        pet.AnyoneCanRide = 1;
        pet.Saddle = 1;
        pet.Energy = pet.Nutrition = 100;
        pet.ExperienceLevels = [100, 200, 400, 600];
        pet.PlacedInRoom = true;
        horse.BotAi = new ExternalLifecycleBotAi();
        horse.BotAi.Init(50, horse.VirtualId, RoomId, horse, _room);
        var pets = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_pets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        Assert.True(pets.TryAdd(pet.PetId, horse));

        return horse;
    }

    private void EstablishExternalMountedGroup(RoomUser rider, RoomUser horse)
    {
        rider.RidingHorse = horse.RidingHorse = true;
        rider.HorseId = horse.VirtualId;
        horse.HorseId = rider.VirtualId;
        using var owner = RoomOwnerScope.Enter(_room);
        var navigation = _room.GetGameMap().Navigation!;
        navigation.ForcePlace(rider, rider.X, rider.Y, 0, ForceResolution.Highest);
        navigation.ForcePlace(horse, horse.X, horse.Y, 0, ForceResolution.Highest);
    }

    private void RideExternalHorse(RoomUser horse, bool mount)
        => new RideHorseEvent(new HorseRidingService(Proxy<IPetLocale>((_, _) => new[] { "horse" })))
            .Parse(_room, _client, ClientPacket(horse.PetData.PetId, mount)).GetAwaiter().GetResult();

    private static int DrainRemainingSearches(RoomNavigation navigation)
    {
        var calls = 0;
        navigation.Executor.Context.Scheduler.Run(int.MaxValue, _ => true,
            _ => { calls++; return new(PathOutcome.BudgetCancelled, 0); }, (_, _) => { });

        return calls;
    }

    private void InitializeExternalUnloadCollections()
    {
        _room.MutedUsers = new();
        _room.UsersWithRights = [];
        _room.WordFilterList = [];
        Set("_tents", new Dictionary<uint, List<RoomUser>>());
    }

    private sealed class ExternalLifecycleBotAi : BotAi
    {
        public override void OnSelfEnterRoom() { }
        public override void OnSelfLeaveRoom(bool kicked) { }
        public override void OnUserEnterRoom(RoomUser user) { }
        public override void OnUserLeaveRoom(GameClient client) { }
        public override void OnUserSay(RoomUser user, string message) { }
        public override void OnUserShout(RoomUser user, string message) { }
        public override void OnTimerTick() { }
    }

    private sealed class PetPickupConnection : DbConnection
    {
        private ConnectionState _state;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override ConnectionState State => _state;
        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) { }
        protected override DbCommand CreateDbCommand() => new NoOpCommand { Connection = this };
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new PetPickupTransaction(this, isolationLevel);
    }

    private sealed class PetPickupTransaction(DbConnection connection, IsolationLevel isolation) : DbTransaction
    {
        protected override DbConnection DbConnection => connection;
        public override IsolationLevel IsolationLevel => isolation;
        public override void Commit() { }
        public override void Rollback() { }
    }
}
