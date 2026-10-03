using System.Collections.Concurrent;
using System.Reflection;
using Plus.HabboHotel;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users.Permissions;
using System.Runtime.CompilerServices;
using Plus.Core.Settings;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.Tests.Performance;
using Xunit;

namespace Plus.Tests;

[CollectionDefinition("Legacy movement", DisableParallelization = true)]
public class LegacyMovementCollection;

[Collection("Legacy movement")]
public class LegacyWalkValidationTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void BlockedExecutionRejectsTheStepAndRequestsRecalculation(byte state, bool end)
    {
        var (room, map) = Create("000\r000\r000");
        var user = new RoomUser(0, 0, 0, room);
        user.Path.Add(new(2, 1));
        map.SetFloorStatus(2, 1, state);
        Assert.False(map.IsValidStep2(user, new(1, 1), new(2, 1), end, false));
        Assert.Empty(user.Path);
        Assert.True(user.PathRecalcNeeded);
    }

    [Fact]
    public void RefusedHeightStepDoesNotConsumeTheRouteEntry()
    {
        var f = Walking();
        f.Map.Model.SqFloorHeight[2, 1] = 2;
        f.Manager.OnCycle();
        Assert.False(f.Users[0].SetStep);
        Assert.Equal(1, f.Users[0].PathStep);
    }

    [Theory]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 3)]
    [InlineData(true, true, 3)]
    public void FastWalkingConsumesExactlyItsValidatedEdges(bool fast, bool super, int edges)
    {
        var f = Walking(new(0, 1), new(1, 1), new(2, 1), new(3, 1));
        var user = f.Users[0];
        user.FastWalking = fast;
        user.SuperFastWalking = super;
        f.Manager.OnCycle();
        Assert.True(user.SetStep);
        Assert.Equal(edges + 1, user.PathStep);
        Assert.Equal(3 - edges, user.SetX);
        f.Manager.OnCycle();
        Assert.Equal(3 - edges, user.X);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, false, true)]
    public void FastWalkingStopsBeforeBlockedIntermediateTiles(byte state, bool fast, bool super)
    {
        var f = Walking(new(0, 1), new(1, 1), new(2, 1), new(3, 1));
        var user = f.Users[0];
        user.FastWalking = fast;
        user.SuperFastWalking = super;
        f.Map.SetFloorStatus(1, 1, state);
        f.Manager.OnCycle();
        Assert.True(user.SetStep);
        Assert.Equal(2, user.SetX);
        Assert.Equal(2, user.PathStep);
    }

    [Fact]
    public void FastWalkingCannotJumpAnUpwardCliffOrAHeightPeak()
    {
        var f = Walking(new(0, 1), new(1, 1), new(2, 1), new(3, 1));
        var user = f.Users[0];
        user.SuperFastWalking = true;
        f.Map.Model.SqFloorHeight[1, 1] = 2;
        f.Manager.OnCycle();
        Assert.True(user.SetStep);
        Assert.Equal(2, user.SetX);
        Assert.Equal(2, user.PathStep);
    }

    [Fact]
    public void PendingFastMovementRechecksIntermediateEdges()
    {
        var f = Walking(new(0, 1), new(1, 1), new(2, 1), new(3, 1));
        var user = f.Users[0];
        user.SuperFastWalking = true;
        f.Manager.OnCycle();
        f.Map.SetFloorStatus(2, 1, 0);
        f.Manager.OnCycle();
        Assert.Equal(3, user.X);
        Assert.False(user.SetStep);
    }

    [Fact]
    public void PendingFastMovementCommitsItsStillValidPrefix()
    {
        var f = Walking(new(0, 1), new(1, 1), new(2, 1), new(3, 1));
        var user = f.Users[0];
        user.SuperFastWalking = true;
        f.Manager.OnCycle();
        f.Map.SetFloorStatus(1, 1, 0);
        f.Manager.OnCycle();
        Assert.Equal(2, user.X);
        Assert.Equal(1, user.Y);
    }

    [Fact]
    public void FastWalkingFiresHooksOnlyOnDepartureAndArrivalTiles()
    {
        var f = Walking(new(0, 1), new(1, 1), new(2, 1), new(3, 1));
        var user = f.Users[0];
        user.GetClient().GetHabbo().Id = user.HabboId;
        user.SuperFastWalking = true;
        var wired = new WiredComponent(f.Room);
        RoomPerformanceFixture.SetField(f.Room, "_wiredComponent", wired);
        var engine = (WiredStackEngine)typeof(WiredComponent)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
        var events = new List<(WiredEventKind Kind, uint ItemId)>();
        engine.ObserveEvent = (evt, _) =>
        {
            if (evt.Kind is WiredEventKind.WalkOn or WiredEventKind.WalkOff)
                events.Add((evt.Kind, evt.EventItem!.Id));
        };
        for (var x = 0; x < 4; x++)
            AddItem(f.Room, f.Map, new Item
            {
                Id = (uint)x + 1, GetX = x, GetY = 1,
                Definition = new ItemDefinition { InteractionType = InteractionType.None, Walkable = true }
            });
        f.Manager.OnCycle();
        Assert.Empty(events);
        f.Manager.OnCycle();
        Assert.Equal(0, user.X);
        Assert.Equal(new[] { (WiredEventKind.WalkOff, 4u), (WiredEventKind.WalkOn, 1u) }, events);
    }

    [Fact]
    public void OrdinaryExecutionRejectsNonAdjacentSteps()
    {
        var (room, map) = Create("0000\r0000\r0000");
        Assert.False(map.IsValidStep2(new(0, 0, 0, room), new(1, 1), new(3, 1), true, false));
    }

    [Theory]
    [InlineData("000\r00x\r0x0", false)]
    [InlineData("000\r000\r0x0", false)]
    [InlineData("000\r002\r020", false)]
    [InlineData("000\r002\r000", true)]
    [InlineData("000\r000\r000", true)]
    public void OfficialCornersRejectVoidAndTwoHighFlanks(string floor, bool expected)
    {
        var (room, map) = Create(floor);
        var user = new RoomUser(0, 0, 0, room);
        Assert.Equal(expected, map.IsValidStep(new(1, 1), new(2, 2), true, false));
        Assert.Equal(expected, map.IsValidStep2(user, new(1, 1), new(2, 2), true, false));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, true)]
    [InlineData(2, 3, false)]
    public void OfficialCornersUseTransitStatesWithoutUserOccupancy(byte a, byte b, bool expected)
    {
        var (room, map) = Create("000\r000\r000");
        var user = new RoomUser(0, 0, 0, room);
        map.SetFloorStatus(2, 1, a);
        map.SetFloorStatus(1, 2, b);
        Assert.Equal(expected, map.IsValidStep(new(1, 1), new(2, 2), true, false));
        Assert.Equal(expected, map.IsValidStep2(user, new(1, 1), new(2, 2), true, false));
        map.SetFloorStatus(2, 1, 1);
        map.SetFloorStatus(1, 2, 1);
        map.GameMap[2, 1] = map.GameMap[1, 2] = 0;
        map.AddUserToMap(user, new(2, 1));
        map.AddUserToMap(user, new(1, 2));
        Assert.True(map.IsValidStep(new(1, 1), new(2, 2), true, false));
        Assert.True(map.IsValidStep2(user, new(1, 1), new(2, 2), true, false));
    }

    [Theory]
    [InlineData("official", true)]
    [InlineData("strict", false)]
    [InlineData("none", true)]
    [InlineData("0", true)]
    public void CornerSettingUsesSettingsManagerAndDefaultsToOfficial(string setting, bool expected)
    {
        var field = typeof(PlusEnvironment).GetField("_settingsManager", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = field.GetValue(null);
        var settings = new SettingsManager(null!, null!);
        typeof(SettingsManager).GetField("_settings", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(settings, new Dictionary<string, string> { ["pathfinding.corner_rule"] = setting });
        field.SetValue(null, settings);
        try
        {
            var (room, map) = Create("000\r000\r000");
            map.SetFloorStatus(2, 1, 0);
            Assert.Equal(expected, map.IsValidStep(new(1, 1), new(2, 2), true, false));
            Assert.Equal(expected, map.IsValidStep2(new(0, 0, 0, room), new(1, 1), new(2, 2), true, false));
            var (_, voidMap) = Create("000\r00x\r000");
            Assert.Equal(setting == "none", voidMap.IsValidStep(new(1, 1), new(2, 2), true, false));
        }
        finally { field.SetValue(null, previous); }
    }

    [Fact]
    public void MissingCornerSettingDefaultsToOfficial()
    {
        var settings = new SettingsManager(null!, null!);
        Assert.Equal("official", settings.TryGetValue("pathfinding.corner_rule"));
        Assert.Equal("0", settings.TryGetValue("unrelated.missing.setting"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuildGateMembershipStillControlsExecution(bool member)
    {
        var f = Walking();
        var user = f.Users[0];
        var group = (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
        RoomPerformanceFixture.SetField(group, "_members", new List<int>());
        RoomPerformanceFixture.SetField(group, "_administrators", new List<int>());
        if (member)
            ((List<int>)typeof(Group).GetField("_members", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group)!).Add(user.GetClient().GetHabbo().Id);
        var gate = AddItem(f.Room, f.Map, new Item
        {
            Id = 1, GroupId = 7, GetX = 2, GetY = 1,
            ExtraData = new LegacyDataFormat { Data = "0" },
            Definition = new ItemDefinition { InteractionType = InteractionType.GuildGate }
        });
        // Keep this validation fixture independent of furniture packet encoding.
        ((ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Manager)!).Clear();
        var groups = Proxy<IGroupManager>((method, args) =>
        {
            Assert.Equal("TryGetGroup", method);
            args[1] = group;
            return true;
        });
        var game = Proxy<IGame>((method, _) => method == "get_GroupManager" ? groups : throw new InvalidOperationException(method));
        var field = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        field.SetValue(null, game);
        try
        {
            f.Map.SetFloorStatus(2, 1, 0);
            Assert.True(f.Map.IsValidStep(new(1, 1), new(2, 1), false, false));
            Assert.Equal(member, f.Map.IsValidStep2(user, new(1, 1), new(2, 1), false, false));
            Assert.Equal(member ? "1" : "0", gate.LegacyDataString);
            Assert.False(user.PathRecalcNeeded);
            if (member)
            {
                Assert.Equal(user.GetClient().GetHabbo().Id, gate.InteractingUser);
                Assert.Equal(4, gate.UpdateCounter);
            }
            else Assert.Empty(user.Path);
        }
        finally { field.SetValue(null, previous); }
    }

    [Fact]
    public void ChairTransitExceptionAndGoalOnlyStatesRemainUnchanged()
    {
        var f = Walking();
        var chair = AddItem(f.Room, f.Map, new Item
        {
            Id = 1, GetX = 2, GetY = 1,
            Definition = new ItemDefinition { IsSeat = true }
        });
        f.Map.SetFloorStatus(2, 1, 3);
        Assert.True(f.Map.IsValidStep2(f.Users[0], new(1, 1), new(2, 1), false, false));
        chair.Definition.IsSeat = false;
        Assert.True(f.Map.IsValidStep2(f.Users[0], new(1, 1), new(2, 1), true, false));
        f.Map.SetFloorStatus(2, 1, 2);
        Assert.True(f.Map.IsValidStep2(f.Users[0], new(1, 1), new(2, 1), true, false));
    }

    [Fact]
    public void VoidFlankWithFurnitureSupportAndInclusiveStepHeightIsOpen()
    {
        var (room, map) = Create("000\r00x\r000");
        var support = AddItem(room, map, new Item
        {
            Id = 1, GetX = 2, GetY = 1, GetZ = 1.5,
            Definition = new ItemDefinition { Walkable = true }
        });
        map.SetFloorStatus(2, 1, 1);
        map.SetFloorStatus(1, 2, 0);
        Assert.True(map.IsValidStep(new(1, 1), new(2, 2), true, false));
        Assert.True(map.IsValidStep2(new(0, 0, 0, room), new(1, 1), new(2, 2), true, false));
        support.GetZ = 1.51;
        Assert.False(map.IsValidStep(new(1, 1), new(2, 2), true, false));
        Assert.False(map.IsValidStep2(new(0, 0, 0, room), new(1, 1), new(2, 2), true, false));
    }

    [Fact]
    public void ClearMovementCancelsTheStoredRouteAndRecalculation()
    {
        var (room, _) = Create("000\r000\r000");
        var user = new RoomUser(0, 0, 0, room) { IsWalking = true, PathRecalcNeeded = true, PathStep = 4, SetStep = true };
        user.Path.Add(new(2, 2));
        user.ClearMovement(true);
        Assert.Empty(user.Path);
        Assert.False(user.PathRecalcNeeded);
        Assert.Equal(1, user.PathStep);
        Assert.False(user.SetStep);
    }

    [Fact]
    public void TeleportUsesTargetHeightInsteadOfPreviousGoal()
    {
        var f = RoomPerformanceFixture.Create(1, 0);
        RoomPerformanceFixture.SetField(f.Room, "_roomItemHandling", new RoomItemHandling(f.Room));
        var user = f.Bots[0];
        user.GoalX = user.GoalY = 0;
        user.TeleportEnabled = true;
        f.Map.Model.SqFloorHeight[2, 1] = 3;
        user.MoveTo(2, 1);
        Assert.Equal(3, user.Z);
        Assert.Equal(2, user.X);
    }

    [Fact]
    public void LyingWalkerRemovesLayStatus()
    {
        var f = Walking();
        var user = f.Users[0];
        user.IsLying = true;
        user.SetStatus("lay", "1.0 null");
        f.Manager.OnCycle();
        Assert.True(user.SetStep);
        Assert.False(user.IsLying);
        Assert.False(user.Statusses.ContainsKey("lay"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MountedFastMovementKeepsHorseOriginAndPathIndex(bool blockSecondEdge, bool riderFirst)
    {
        var f = RoomPerformanceFixture.Create(1, 1);
        RoomPerformanceFixture.SetField(f.Room, "_roomItemHandling", new RoomItemHandling(f.Room));
        f.Map.GenerateMaps();
        var rider = f.Users[0];
        var horse = f.Bots[0];
        rider.GetClient().GetHabbo().Permissions = new PermissionComponent(new(), new());
        rider.GetClient().GetHabbo().Id = rider.HabboId;
        rider.SetPos(3, 1, 0);
        horse.SetPos(3, 1, 0);
        rider.AllowOverride = false;
        rider.RidingHorse = true;
        rider.HorseId = horse.VirtualId;
        rider.SuperFastWalking = true;
        rider.IsWalking = true;
        rider.GoalX = 0;
        rider.GoalY = 1;
        rider.Path = [new(0, 1), new(1, 1), new(2, 1), new(3, 1)];
        f.Map.SetFloorStatus(1, 1, 1);
        f.Manager.OnCycle();
        Assert.True(horse.SetStep);
        if (riderFirst)
        {
            // Commit rider first, which moves the horse before its own pending validation.
            var roster = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
                .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Manager)!;
            roster.Clear();
            rider.VirtualId = 0;
            horse.VirtualId = 1;
            rider.HorseId = horse.VirtualId;
            roster[0] = rider;
            roster[1] = horse;
        }
        rider.Freezed = true; // Commit the announced batch without starting another one.
        horse.IsWalking = true; // The rider's destination must not see an idle occupying horse.
        horse.Freezed = true;
        if (blockSecondEdge) f.Map.SetFloorStatus(1, 1, 0);
        f.Manager.OnCycle();
        Assert.Equal(blockSecondEdge ? 2 : 0, horse.X);
        Assert.Equal(1, horse.PathStep);
        Assert.False(horse.SetStep);
    }

    [Fact]
    public void RiderWithMissingHorseStillStagesMovement()
    {
        var f = Walking();
        var user = f.Users[0];
        user.RidingHorse = true;
        user.HorseId = 99;
        f.Manager.OnCycle();
        Assert.True(user.SetStep);
        Assert.Equal(2, user.SetX);
    }

    [Fact]
    public void OpenFloorIsDiagonalFirstAndReturnsReversedRouteIncludingStart()
    {
        var (room, map) = Create("000000\r000000\r000000\r000000");
        var user = new RoomUser(0, 0, 0, room);
        var path = PathFinder.FindPath(user, true, map, new(0, 1), new(5, 3));
        Assert.Equal(new[] { new Vector2D(5, 3), new(4, 3), new(3, 3), new(2, 3), new(1, 2), new(0, 1) }, path);
        Assert.Equal(new[] { new Vector2D(1, 1) }, PathFinder.FindPath(user, true, map, new(1, 1), new(1, 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacySearchMatchesShortestRoutesInSeededMazes(bool diagonal)
    {
        var random = new Random(714);
        for (var sample = 0; sample < 150; sample++)
        {
            var (room, map) = Create(string.Join('\r', Enumerable.Repeat("00000000", 8)));
            map.SetFloorStatus(0, 0, 1);
            var user = new RoomUser(0, 0, 0, room);
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                if (random.Next(4) == 0) map.SetFloorStatus(x, y, 0);
            map.SetFloorStatus(0, 0, 1);
            map.SetFloorStatus(7, 7, 1);
            var expected = ShortestDistance(map, diagonal, new(0, 0), new(7, 7));
            var path = PathFinder.FindPath(user, diagonal, map, new(0, 0), new(7, 7));
            Assert.True(expected < 0 ? path.Count == 0 : path.Count == expected + 1,
                $"Maze {sample}, diagonal={diagonal}: shortest {expected}, returned {path.Count - 1}");
            if (path.Count > 0)
            {
                Assert.Equal(new(7, 7), path[0]);
                Assert.Equal(new(0, 0), path[^1]);
                for (var i = path.Count - 1; i > 0; i--)
                    Assert.True(map.IsValidStep(path[i], path[i - 1], i == 1, false));
            }
        }
    }

    private static int ShortestDistance(Gamemap map, bool diagonal, Vector2D start, Vector2D goal)
    {
        var queue = new Queue<(Vector2D Tile, int Distance)>();
        var seen = new HashSet<Vector2D> { start };
        queue.Enqueue((start, 0));
        while (queue.TryDequeue(out var current))
        {
            if (current.Tile.Equals(goal)) return current.Distance;
            foreach (var offset in diagonal ? PathFinder.DiagMovePoints : PathFinder.NoDiagMovePoints)
            {
                var to = current.Tile + offset;
                if (map.IsValidStep(current.Tile, to, to.Equals(goal), false) && seen.Add(to))
                    queue.Enqueue((to, current.Distance + 1));
            }
        }
        return -1;
    }

    private static Item AddItem(Room room, Gamemap map, Item item)
    {
        item.Definition.AdjustableHeights = new List<double>();
        item.Definition.ItemName = "";
        RoomPerformanceFixture.SetField(item, "_room", room);
        var items = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling)
            .GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        items[item.Id] = item;
        map.AddCoordinatedItem(item, new(item.GetX, item.GetY));
        return item;
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Call = call;
        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }

    private static RoomPerformanceFixture Walking(params Vector2D[] path)
    {
        var f = RoomPerformanceFixture.Create(0, 1);
        RoomPerformanceFixture.SetField(f.Room, "_roomItemHandling", new RoomItemHandling(f.Room));
        f.Map.GenerateMaps();
        var user = f.Users[0];
        user.AllowOverride = false;
        user.GetClient().GetHabbo().Permissions = new PermissionComponent(new(), new());
        var origin = user.Coordinate;
        user.SetPos(path.Length == 0 ? 1 : 3, 1, 0);
        f.Map.UpdateUserMovement(origin, user.Coordinate, user);
        f.Map.SetFloorStatus(1, 1, 1);
        user.Path = path.Length == 0 ? [new(3, 1), new(2, 1), new(1, 1)] : path.ToList();
        user.GoalX = user.Path[0].X;
        user.GoalY = user.Path[0].Y;
        user.IsWalking = true;
        return f;
    }

    private static (Room Room, Gamemap Map) Create(string floor)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var map = new Gamemap(room, new RoomModel("test", 0, 0, 0, 0, floor, false, 0, false));
        RoomPerformanceFixture.SetField(room, "_gamemap", map);
        RoomPerformanceFixture.SetField(room, "_roomUserManager", new RoomUserManager(room));
        RoomPerformanceFixture.SetField(room, "_roomItemHandling", new RoomItemHandling(room));
        return (room, map);
    }
}
