using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Rooms.AI.Types;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public class BotRoamingTests
{
    [Fact]
    public void IncludesBorderAndDoorLineTilesAndNeverFallsBackToABlockedCorner()
    {
        var (_, map) = Create("00\r00", 0, 0);
        var squares = map.WalkableSquares();
        Assert.Equal(new[] { new Point(1, 0), new Point(0, 1), new Point(1, 1) }, squares);
        Assert.DoesNotContain(new Point(0, 0), squares);

        var seen = new HashSet<Point>();
        for (var i = 0; i < 300; i++)
        {
            Assert.True(map.TryGetRandomWalkableSquare(out var square));
            seen.Add(square);
            Assert.NotEqual(new Point(0, 0), square);
        }
        Assert.True(seen.SetEquals(squares));
    }

    [Fact]
    public void KeepsOpenTilesOnTheDoorRowAndDoorColumn()
    {
        var (_, doorRow) = Create("xxx\r000\rxxx", 0, 1);
        Assert.Equal(new[] { new Point(1, 1), new Point(2, 1) }, doorRow.WalkableSquares());

        var (_, doorColumn) = Create("x0x\rx0x\rx0x", 1, 0);
        Assert.Equal(new[] { new Point(1, 1), new Point(1, 2) }, doorColumn.WalkableSquares());
        Assert.False(doorColumn.WalkableSquares().Contains(new Point(0, 0)));
    }

    [Fact]
    public void ReturnsNoTargetWhenNothingIsWalkable()
    {
        var (_, map) = Create("xxx\rxxx\rxxx", 1, 1);
        Assert.Empty(map.WalkableSquares());
        Assert.False(map.TryGetRandomWalkableSquare(out _));
        Assert.Equal(new Point(0, 0), map.GetRandomWalkableSquare());
    }

    [Fact]
    public void CrowdZeroingLiveTilesDoesNotShrinkOrRebuildTargets()
    {
        var (_, map) = Create("x00\rx00\rx00", 1, 1);
        var before = map.WalkableSquares();
        Assert.DoesNotContain(new Point(0, 0), before);
        Assert.Contains(new Point(2, 2), before);

        for (var pass = 0; pass < 25; pass++)
        {
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
                map.GameMap[x, y] = 0;
            Assert.Same(before, map.WalkableSquares());
        }

        var seen = new HashSet<Point>();
        for (var i = 0; i < 200; i++)
        {
            Assert.False(map.TryGetRandomWalkableSquare(out _));
            Assert.True(map.TryGetRandomWalkableSquare(true, out var square));
            Assert.NotEqual(new Point(0, 0), square);
            Assert.Contains(square, before);
            seen.Add(square);
        }
        Assert.True(seen.SetEquals(before));
    }

    [Fact]
    public void TerrainStatusReachesStressTargetsAndLiveOccupancyDoesNot()
    {
        var (_, map) = Create("000\r000\r000", 1, 1);
        map.GameMap[0, 0] = 0;
        for (var i = 0; i < 200; i++)
        {
            Assert.True(map.TryGetRandomWalkableSquare(out var live));
            Assert.NotEqual(new Point(0, 0), live);
        }
        Assert.Contains(new Point(0, 0), map.WalkableSquares());
        Assert.True(map.TryGetRandomWalkableSquare(true, out var stress));
        Assert.Contains(stress, map.WalkableSquares());

        map.SetFloorStatus(2, 2, 0);
        Assert.Equal(0, map.GameMap[2, 2]);
        Assert.DoesNotContain(new Point(2, 2), map.WalkableSquares());
        for (var i = 0; i < 200; i++)
        {
            Assert.True(map.TryGetRandomWalkableSquare(out var live));
            Assert.NotEqual(new Point(2, 2), live);
            Assert.True(map.TryGetRandomWalkableSquare(true, out var open));
            Assert.NotEqual(new Point(2, 2), open);
        }
    }

    [Fact]
    public void GateCloseAndOpenUpdateTheCacheAndTemporarySteps()
    {
        var (room, map) = Create("000\r000\r000", 1, 1);
        var stress = Bot(room, allowOverride: true);
        var placed = Bot(room, allowOverride: true, temporary: false);
        var from = new Vector2D(1, 2);
        var gate = new Vector2D(2, 2);

        Assert.Contains(new Point(2, 2), map.WalkableSquares());
        Assert.True(map.IsValidStep(from, gate, true, true, false, stress));
        Assert.True(map.IsValidStep2(stress, from, gate, true, true));

        map.SetFloorStatus(2, 2, 0);
        Assert.Equal(0, map.GameMap[2, 2]);
        Assert.DoesNotContain(new Point(2, 2), map.WalkableSquares());
        Assert.False(map.IsValidStep(from, gate, true, true, false, stress));
        Assert.False(map.IsValidStep2(stress, from, gate, true, true));
        Assert.True(map.IsValidStep2(placed, from, gate, true, true));

        map.SetFloorStatus(2, 2, 1);
        Assert.Equal(1, map.GameMap[2, 2]);
        Assert.Contains(new Point(2, 2), map.WalkableSquares());
        Assert.True(map.IsValidStep(from, gate, true, true, false, stress));
        Assert.True(map.IsValidStep2(stress, from, gate, true, true));

        map.GameMap[2, 2] = 0;
        Assert.Contains(new Point(2, 2), map.WalkableSquares());
        Assert.True(map.IsValidStep2(stress, from, gate, true, true));
        var seenStress = false;
        for (var i = 0; i < 200; i++)
        {
            Assert.True(map.TryGetRandomWalkableSquare(out var live));
            Assert.NotEqual(new Point(2, 2), live);
            Assert.True(map.TryGetRandomWalkableSquare(true, out var open));
            if (open == new Point(2, 2))
                seenStress = true;
        }
        Assert.True(seenStress);
    }

    [Fact]
    public void OrdinaryGenericBotDoesNotTargetAnOccupiedTile()
    {
        var (room, map) = Create("000\r000\r000", 1, 1);
        map.GameMap[0, 0] = 0;
        var user = Bot(room, allowOverride: false, temporary: false);
        user.SetPos(1, 2, 0);
        user.GoalX = -1;
        user.GoalY = -1;
        var ai = new GenericBot(user.VirtualId);
        ai.Init(user.BotData.BotId, user.VirtualId, 1, user, room);
        var timer = typeof(GenericBot).GetField("_actionTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(timer);
        for (var i = 0; i < 200; i++)
        {
            timer.SetValue(ai, 0);
            ai.OnTimerTick();
            var goal = new Point(user.GoalX, user.GoalY);
            Assert.NotEqual(new Point(0, 0), goal);
            Assert.Equal(1, map.GameMap[goal.X, goal.Y]);
        }
    }

    [Fact]
    public void FloorStatusChangesInvalidateTheCachedTargets()
    {
        var (_, map) = Create("000\r000\r000", 1, 1);
        var before = map.WalkableSquares();
        Assert.Same(before, map.WalkableSquares());
        Assert.Contains(new Point(0, 0), before);

        map.SetFloorStatus(0, 0, 0);
        var blocked = map.WalkableSquares();
        Assert.NotSame(before, blocked);
        Assert.DoesNotContain(new Point(0, 0), blocked);

        map.SetFloorStatus(2, 2, 2);
        map.SetFloorStatus(2, 0, 3);
        var openOnly = map.WalkableSquares();
        Assert.DoesNotContain(new Point(2, 2), openOnly);
        Assert.DoesNotContain(new Point(2, 0), openOnly);
        map.SetFloorStatus(2, 2, 1);
        Assert.Contains(new Point(2, 2), map.WalkableSquares());
    }

    [Fact]
    public void GenerateMapsRebuildsOpenFloorAndOccupancyWritesDoNot()
    {
        var (room, map) = Create("000\r000\r000", 1, 1);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused));
        map.SetFloorStatus(0, 0, 0);
        Assert.DoesNotContain(new Point(0, 0), map.WalkableSquares());

        map.GenerateMaps();
        var rebuilt = map.WalkableSquares();
        Assert.Contains(new Point(0, 0), rebuilt);
        Assert.DoesNotContain(new Point(1, 1), rebuilt);

        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 3; x++)
            map.GameMap[x, y] = 0;
        Assert.Same(rebuilt, map.WalkableSquares());
    }

    [Fact]
    public void GenerateMapsLeavesTemporaryTilesOpenAndReservesOrdinaryUsers()
    {
        var (room, map) = Create("000\r000\r000", 1, 1);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems));
        var users = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, users);
        var roster = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(users)!;

        var stress = Bot(room, allowOverride: true);
        stress.SetPos(0, 0, 0);
        stress.InternalRoomId = 1;
        stress.SqState = 3;
        roster.TryAdd(stress.InternalRoomId, stress);

        var ordinary = new RoomUser(0, 1, 2, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        ordinary.SetPos(2, 2, 0);
        ordinary.InternalRoomId = 2;
        ordinary.SqState = 3;
        roster.TryAdd(ordinary.InternalRoomId, ordinary);

        var placed = Bot(room, allowOverride: false, temporary: false);
        placed.SetPos(0, 2, 0);
        placed.InternalRoomId = 3;
        placed.SqState = 3;
        roster.TryAdd(placed.InternalRoomId, placed);

        map.GenerateMaps();

        Assert.Equal(1, map.GameMap[0, 0]);
        Assert.Equal(3, stress.SqState);
        Assert.Equal(0, map.GameMap[2, 2]);
        Assert.Equal(1, ordinary.SqState);
        Assert.Equal(0, map.GameMap[0, 2]);
        Assert.Equal(1, placed.SqState);
        Assert.Contains(new Point(0, 0), map.WalkableSquares());
        Assert.Contains(new Point(2, 2), map.WalkableSquares());
        Assert.True(map.IsValidStep2(stress, new(2, 0), new(0, 0), true, true));
    }

    [Fact]
    public void OnlyTemporaryBotsLoseWallOverride()
    {
        var (room, map) = Create("xxxxx\rx0x0x\rxxxxx", 1, 1);
        var placed = Bot(room, allowOverride: true, temporary: false);
        Assert.True(map.IsValidStep2(placed, new(1, 1), new(2, 1), false, true));
        Assert.True(map.IsValidStep(new(1, 1), new(2, 1), false, true, false, placed));
        var through = PathFinder.FindPath(placed, true, map, new(1, 1), new(3, 1));
        Assert.Contains(through, step => step.X == 2 && step.Y == 1);

        var stress = Bot(room, allowOverride: true, temporary: true);
        Assert.False(map.IsValidStep2(stress, new(1, 1), new(2, 1), false, true));
        var around = PathFinder.FindPath(stress, true, map, new(1, 1), new(3, 1));
        Assert.DoesNotContain(around, step => step.X == 2 && step.Y == 1);
    }

    [Fact]
    public void StressBotPathsAroundWallsAndCliffsWhileStaffOverrideMayCrossThem()
    {
        var (room, map) = Create("xxxxx\rx000x\rx0x0x\rx000x\rxxxxx", 1, 1);
        var stress = Bot(room, allowOverride: true);
        stress.SetPos(1, 2, 0);
        var around = PathFinder.FindPath(stress, true, map, new(1, 2), new(3, 2));
        Assert.Contains(around, step => step.X == 3 && step.Y == 2);
        Assert.DoesNotContain(around, step => step.X == 2 && step.Y == 2);

        for (var y = 0; y < 5; y++)
        for (var x = 0; x < 5; x++)
        {
            map.GameMap[x, y] = 0;
            map.AddUserToMap(Bot(room, allowOverride: false), new(x, y));
        }
        var crowded = PathFinder.FindPath(stress, true, map, new(1, 2), new(3, 2));
        Assert.Contains(crowded, step => step.X == 3 && step.Y == 2);
        Assert.DoesNotContain(crowded, step => step.X == 2 && step.Y == 2);

        var staff = new RoomUser(0, 1, 2, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { AllowOverride = true };
        staff.SetPos(1, 2, 0);
        var through = PathFinder.FindPath(staff, true, map, new(1, 2), new(3, 2));
        Assert.Contains(through, step => step.X == 2 && step.Y == 2);
    }

    [Fact]
    public void TemporaryOverrideRejectsWallsAndHeightAndAcceptsOccupiedFloor()
    {
        var (room, map) = Create("xxxxx\rx0x0x\rx020x\rxxxxx", 1, 1);
        var bot = Bot(room, allowOverride: true);
        Assert.False(map.IsValidStep2(bot, new(1, 1), new(2, 1), false, true));
        Assert.False(map.IsValidStep(new(1, 1), new(2, 1), false, true, false, bot));
        Assert.False(map.IsValidStep2(bot, new(1, 2), new(2, 2), true, true));

        map.GameMap[3, 1] = 0;
        map.AddUserToMap(Bot(room, allowOverride: false), new(3, 1));
        Assert.True(map.IsValidStep2(bot, new(1, 1), new(3, 1), true, true));
        Assert.True(map.IsValidStep(new(1, 1), new(3, 1), true, true, false, bot));

        var staff = new RoomUser(0, 1, 3, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { AllowOverride = true };
        Assert.True(map.IsValidStep2(staff, new(1, 1), new(2, 1), false, true));
        Assert.True(map.IsValidStep(new(1, 1), new(2, 1), false, true, false, staff));
    }

    [Fact]
    public void StressBotDoesNotClimbAStepTallerThanTheWalkLimit()
    {
        var (room, map) = Create("xxxxx\rx020x\rxxxxx", 1, 1);
        var stress = Bot(room, allowOverride: true);
        stress.SetPos(1, 1, 0);
        Assert.Empty(PathFinder.FindPath(stress, true, map, new(1, 1), new(3, 1)));
    }

    [Fact]
    public void GenericBotRoamsARealTileAndStaysPutWhenTheFloorIsBlocked()
    {
        var (room, map) = Create("x00\rx00\rx00", 1, 1);
        var user = Bot(room, allowOverride: true);
        user.SetPos(1, 2, 0);
        user.GoalX = -1;
        user.GoalY = -1;
        var ai = new GenericBot(user.VirtualId);
        ai.Init(user.BotData.BotId, user.VirtualId, 1, user, room);
        ai.OnTimerTick();
        Assert.Contains(new Point(user.GoalX, user.GoalY), map.WalkableSquares());
        Assert.NotEqual(new Point(0, 0), new Point(user.GoalX, user.GoalY));

        var (blockedRoom, blockedMap) = Create("xxx\rxxx\rxxx", 1, 1);
        var stuck = Bot(blockedRoom, allowOverride: true);
        stuck.GoalX = 4;
        stuck.GoalY = 4;
        var idle = new GenericBot(stuck.VirtualId);
        idle.Init(stuck.BotData.BotId, stuck.VirtualId, 1, stuck, blockedRoom);
        idle.OnTimerTick();
        Assert.False(blockedMap.TryGetRandomWalkableSquare(out _));
        Assert.Equal(4, stuck.GoalX);
        Assert.Equal(4, stuck.GoalY);
        Assert.False(stuck.PathRecalcNeeded);
    }

    [Fact]
    public void PickingFiveHundredTargetsOnALargeFloorStaysCached()
    {
        var floor = string.Join('\r', Enumerable.Repeat(new string('0', 64), 64));
        var (_, map) = Create(floor, 0, 0);
        var squares = map.WalkableSquares();
        Assert.Equal(64 * 64 - 1, squares.Length);
        Assert.Same(squares, map.WalkableSquares());

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 500; i++)
        {
            Assert.True(map.TryGetRandomWalkableSquare(true, out var square));
            Assert.NotEqual(new Point(0, 0), square);
            Assert.InRange(square.X, 0, 63);
            Assert.InRange(square.Y, 0, 63);
        }
        clock.Stop();
        Assert.True(clock.ElapsedMilliseconds < 200, $"500 cached picks took {clock.ElapsedMilliseconds}ms");
    }

    private static RoomUser Bot(Room room, bool allowOverride, bool temporary = true)
    {
        var speeches = new List<RandomSpeech>();
        var data = new RoomBot(-1, 1, "generic", "freeroam", "Stress", "", "hd-180-1",
            1, 1, 0, 0, 0, 0, 0, 0, ref speeches, "M", 0, 7, false, 60, false, 0)
        { IsTemporary = temporary };
        var user = new RoomUser(0, 1, 1, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused)
        {
            AllowOverride = allowOverride,
            BotData = data
        };
        return user;
    }

    private static (Room Room, Gamemap Map) Create(string heightmap, int doorX, int doorY)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var model = new RoomModel("test", doorX, doorY, 0, 0, heightmap, 0, 0, false);
        var map = new Gamemap(room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        return (room, map);
    }
}
