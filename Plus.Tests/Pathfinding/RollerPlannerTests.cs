using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Users.Permissions;
using Xunit;

namespace Plus.Tests;

// §14.9 roller transport groups. The loop is (1,1) -> (2,1) -> (2,2) -> (1,2) -> (1,1).
public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(PathfindingEngine.V2, false)]
    [InlineData(PathfindingEngine.V2, true)]
    public void LoadedChainOfUsersAndSolidCargoAdvancesAsOneSegment(PathfindingEngine engine, bool reverseIds)
    {
        PrepareRollerChain(8, reverseIds);
        var cargo = PlannerCargo(300, 1, 1);
        InstallRollerChainEngine(engine);
        var head = PlannerActor(1, 2, 1, .5); var tail = PlannerActor(2, 0, 1, .5);
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((1, 1, .5), (tail.X, tail.Y, tail.Z));
        Assert.Equal((2, 1, .5), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((3, 1, .5), (head.X, head.Y, head.Z));
        Assert.Equal(3, PlannerSlides());
    }

    [Theory]
    [InlineData(PathfindingEngine.V2)]
    public void StationaryElevatedCargoHoldsEveryUpstreamLoad(PathfindingEngine engine)
    {
        foreach (var x in new[] { 1, 2, 3 }) PlannerRoller((uint)(10 + x), x, 1, 2);
        var elevated = ExecutorFloor(20, 3, 1, z: .5, height: 1);
        var cargo = PlannerCargo(21, 1, 1);
        InstallRollerChainEngine(engine);
        var actor = PlannerActor(1, 2, 1, .5);
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((3, 1, .5), (elevated.GetX, elevated.GetY, elevated.GetZ));
        Assert.Equal((2, 1, .5), (actor.X, actor.Y, actor.Z));
        Assert.Equal((1, 1, .5), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal(0, PlannerSlides());
    }

    [Theory]
    [InlineData(PathfindingEngine.V2, false, false)]
    [InlineData(PathfindingEngine.V2, true, false)]
    [InlineData(PathfindingEngine.V2, false, true)]
    [InlineData(PathfindingEngine.V2, true, true)]
    public void CompetingFeedsAdmitOnlyTheLowerRollerId(PathfindingEngine engine, bool westWins, bool cargo)
    {
        PlannerRoller(westWins ? 10u : 11u, 0, 1, 2); PlannerRoller(westWins ? 11u : 10u, 1, 0, 4);
        var westCargo = cargo ? PlannerCargo(30, 0, 1) : null; var northCargo = cargo ? PlannerCargo(31, 1, 0) : null;
        InstallRollerChainEngine(engine);
        var observer = PlannerActor(1, 3, 3, 0);
        var west = cargo ? null : PlannerActor(2, 0, 1, .5); var north = cargo ? null : PlannerActor(3, 1, 0, .5);
        StartPlannerRollers(); ExecutorTick();
        var westPosition = cargo ? (westCargo!.GetX, westCargo.GetY, westCargo.GetZ) : (west!.X, west.Y, west.Z);
        var northPosition = cargo ? (northCargo!.GetX, northCargo.GetY, northCargo.GetZ) : (north!.X, north.Y, north.Z);
        Assert.Equal(westWins ? (1, 1, 0d) : (0, 1, .5), westPosition);
        Assert.Equal(westWins ? (1, 0, .5) : (1, 1, 0d), northPosition);
        Assert.Equal(1, PlannerSlides());
        Assert.Equal((3, 3, 0d), (observer.X, observer.Y, observer.Z));
    }

    [Theory]
    [InlineData(PathfindingEngine.V2, false)]
    [InlineData(PathfindingEngine.V2, true)]
    public void FullLoopRotatesUsersAndFurnitureTogetherEveryCycle(PathfindingEngine engine, bool reverseIds)
    {
        var loop = PlannerFullLoop(engine, reverseIds);
        StartPlannerRollers();
        for (var cycle = 1; cycle <= 4; cycle++)
        {
            ExecutorTick();
            Assert.Equal(loop.Expected(cycle), loop.Positions());
            Assert.Equal(4, PlannerSlides());
            Assert.All(loop.Users, user => Assert.Equal(new[] { user }, _room.GetGameMap().GetRoomUsers(new(user.X, user.Y))));
        }
    }

    [Theory]
    [InlineData(PathfindingEngine.V2, false)]
    [InlineData(PathfindingEngine.V2, true)]
    public void PartiallyOccupiedLoopAdvancesDownstreamFirstIntoItsVacancy(PathfindingEngine engine, bool reverseIds)
    {
        PlannerLoopRollers(reverseIds);
        var cargo = PlannerCargo(30, 2, 1);
        InstallRollerChainEngine(engine);
        var first = PlannerActor(1, 1, 1, .5); var second = PlannerActor(2, 2, 2, .5);
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((2, 1, .5), (first.X, first.Y, first.Z));
        Assert.Equal((2, 2, .5), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((1, 2, .5), (second.X, second.Y, second.Z));
        Assert.Equal(3, PlannerSlides());
    }

    [Theory]
    [InlineData(PathfindingEngine.V2)]
    public void LoopWithOneExternallyBlockedExitStaysUnchanged(PathfindingEngine engine)
    {
        var loop = PlannerFullLoop(engine, false, () => Add(40, 0, 2, z: 1, height: 3, stackable: false, width: 2));
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal(loop.Expected(0), loop.Positions());
        Assert.Equal(0, PlannerSlides());
    }

    [Fact]
    public void ExternalClaimOnOneLoopTileLeavesTheWholeLoopUnchanged()
    {
        var loop = PlannerFullLoop(PathfindingEngine.V2, false);
        var outsider = PlannerActor(3, 0, 0, 0);
        StartPlannerRollers();
        var navigation = _room.GetGameMap().Navigation!;
        Assert.True(navigation.Executor.Claims.TryClaim(outsider, navigation.Grid.Tile(2, 1), ClaimKind.Exclusive, TargetOccupancy.None));
        ExecutorTick();
        Assert.Equal(loop.Expected(0), loop.Positions());
        Assert.Equal(0, PlannerSlides());
    }

    [Theory]
    [InlineData(PathfindingEngine.V2)]
    public void FailedLoopParticipantLeavesTheWholeLoopUnchanged(PathfindingEngine engine)
    {
        var loop = PlannerFullLoop(engine, false);
        StartPlannerRollers();
        var walking = loop.Users[1]; walking.IsWalking = true;
        walking.GoalX = walking.X; walking.GoalY = walking.Y;
        ExecutorTick();
        Assert.Equal(loop.Expected(0), loop.Positions());
        Assert.Equal(0, PlannerSlides());
    }

    [Theory]
    [InlineData(PathfindingEngine.V2)]
    public void LoopHooksObserveOnlyTheFullyCommittedRotation(PathfindingEngine engine)
    {
        var loop = PlannerFullLoop(engine, false);
        var observed = new List<string>();
        PlannerObserveWalkOn(() => observed.Add(loop.Positions()));
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal(loop.Expected(1), loop.Positions());
        Assert.NotEmpty(observed);
        Assert.All(observed, snapshot => Assert.Equal(loop.Expected(1), snapshot));
    }

    [Theory]
    [InlineData(PathfindingEngine.V2, false)]
    [InlineData(PathfindingEngine.V2, true)]
    public void FullLoopRotatesWhileItsExternalFeederWaits(PathfindingEngine engine, bool feederHasLowerId)
    {
        var loop = PlannerFullLoop(engine, false, () => PlannerRoller(feederHasLowerId ? 5u : 50u, 0, 1, 2));
        var feeder = PlannerActor(3, 0, 1, .5);
        StartPlannerRollers();
        for (var cycle = 1; cycle <= 2; cycle++)
        {
            ExecutorTick();
            Assert.Equal(loop.Expected(cycle), loop.Positions());
            Assert.Equal((0, 1, .5), (feeder.X, feeder.Y, feeder.Z));
            Assert.Equal(4, PlannerSlides());
            Assert.DoesNotContain(_client.Packets, packet => ExecutorIsAvatarSlide(packet, feeder.VirtualId));
        }
    }

    private sealed record PlannerLoopFixture(RoomUser[] Users, Item[] Cargo)
    {
        // Loads start at loop index 0 (user), 1 (cargo), 2 (user), 3 (cargo) and advance one index per cycle.
        public string Expected(int cycle) => string.Join(' ', Enumerable.Range(0, 4)
            .Select(index => PlannerLoopTiles[(index + cycle) % 4]).Select(tile => $"{tile.X},{tile.Y},0.5"));

        public string Positions() => string.Join(' ', new[]
        {
            Format(Users[0].X, Users[0].Y, Users[0].Z), Format(Cargo[0].GetX, Cargo[0].GetY, Cargo[0].GetZ),
            Format(Users[1].X, Users[1].Y, Users[1].Z), Format(Cargo[1].GetX, Cargo[1].GetY, Cargo[1].GetZ)
        });

        private static string Format(int x, int y, double z) => FormattableString.Invariant($"{x},{y},{z}");
    }

    private static readonly Point[] PlannerLoopTiles = [new(1, 1), new(2, 1), new(2, 2), new(1, 2)];

    private PlannerLoopFixture PlannerFullLoop(PathfindingEngine engine, bool reverseIds, Action? arrange = null,
        bool walkableFirstCargo = false)
    {
        PlannerLoopRollers(reverseIds);
        var first = walkableFirstCargo ? ExecutorFloor(30, 2, 1, z: .5, height: .5) : PlannerCargo(30, 2, 1);
        var cargo = new[] { first, PlannerCargo(31, 1, 2) };
        arrange?.Invoke();
        InstallRollerChainEngine(engine);
        return new([PlannerActor(1, 1, 1, .5), PlannerActor(2, 2, 2, .5)], cargo);
    }

    private void PlannerLoopRollers(bool reverseIds)
    {
        var rotations = new[] { 2, 4, 6, 0 };
        for (var index = 0; index < 4; index++)
            PlannerRoller((uint)(reverseIds ? 13 - index : 10 + index), PlannerLoopTiles[index].X, PlannerLoopTiles[index].Y, rotations[index]);
    }

    private Item PlannerRoller(uint id, int x, int y, int rotation)
    {
        var roller = Furni(id, InteractionType.Roller, WiredBoxType.None);
        roller.Definition.Walkable = true; roller.Definition.Height = .5;
        roller.Definition.Width = roller.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, roller, x, y, rotation, true, false, false));
        return roller;
    }

    // Solid (non-walkable) cargo resting on the roller top.
    private Item PlannerCargo(uint id, int x, int y) => Add(id, x, y, z: .5, height: .5);

    private RoomUser PlannerActor(int id, int x, int y, double z)
    {
        var client = id == 1 ? _client : new TestClient();
        if (id != 1) client.SetHabbo(new Habbo { Id = id + 6, Username = $"planner-{id}", CurrentRoom = _room,
            Access = Plus.HabboHotel.Permissions.UserAccess.Empty });
        var habbo = client.GetHabbo(); habbo.Effects = new EffectsComponent();
        habbo.HabboStats = new HabboStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        var actor = new RoomUser(habbo.Id, RoomId, id, _room) { UserId = habbo.Id, InternalRoomId = id, X = x, Y = y, Z = z };
        typeof(RoomUser).GetField("_mClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(actor, client);
        var roster = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_room.GetRoomUserManager())!;
        Assert.True(roster.TryAdd(id, actor));
        if (_room.GetGameMap().Navigation is { UsesExecutor: true } navigation) navigation.Admit(actor);
        else _room.GetGameMap().AddUserToMap(actor, new(x, y));
        return actor;
    }

    private void StartPlannerRollers()
    {
        ExecutorTick(); EnableExecutorRollers();
    }

    private int PlannerSlides() => _client.Packets.Count(packet => packet.Header == ServerPacketHeader.SlideObjectBundleComposer);

    private void PlannerObserveWalkOn(Action observe)
    {
        var item = Furni(900, InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOnFurni);
        item.Definition.Height = 0; item.Definition.Width = item.Definition.Length = 1;
        Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, item, 3, 3, 0, true, false, false));
        Assert.True(_room.GetWired().AddBox(new PlannerWalkOnObserver(_room, item, observe)));
    }

    private sealed class PlannerWalkOnObserver(Room room, Item item, Action observe) : IWiredItem
    {
        public Room Instance { get; set; } = room;
        public Item Item { get; set; } = item;
        public WiredBoxType Type => WiredBoxType.TriggerWalkOnFurni;
        public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
        public string StringData { get; set; } = "";
        public bool BoolData { get; set; }
        public string ItemsData { get; set; } = "";
        public void HandleSave(IIncomingPacket packet) => throw new NotSupportedException();
        public bool Execute(params object[] arguments)
        {
            observe();
            return false;
        }
    }
}
