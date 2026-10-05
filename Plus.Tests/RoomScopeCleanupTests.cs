using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

[Collection("Placed furni room")]
public sealed class RoomScopeCleanupTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedTeardownReleasesScopeAndAllowsSameRoomToBeCreated(bool failBeforeDispose)
    {
        using var fixture = new Fixture();
        var room = fixture.Create(1);
        if (failBeforeDispose) Set(room, "_wiredComponent", null);
        else room.MutedUsers = null!;

        Assert.Throws<NullReferenceException>(() => fixture.Manager.UnloadRoom(1));
        Assert.Equal(1, fixture.Disposed);
        fixture.Manager.UnloadRoom(1);
        Assert.Equal(1, fixture.Disposed);
        Assert.NotSame(room, fixture.Create(1));
    }

    [Fact]
    public async Task LoadingDuringScopeDisposalRefusesWithoutHoldingOtherRoomOperations()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var fixture = new Fixture(() => { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); });
        fixture.Create(1);
        var unloading = Task.Run(() => fixture.Manager.UnloadRoom(1));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            Assert.False(fixture.Manager.TryLoadRoom(1, out var during));
            Assert.Null(during);
            fixture.Manager.UnloadRoom(1);
            Assert.Equal(1, fixture.Disposed);
            // A different room can complete its admission while this scope is disposing.
            Assert.Equal(2u, fixture.Create(2).Id);
        }
        finally { release.Set(); await unloading.WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal(1, fixture.Disposed);
        Assert.Equal(1u, fixture.Create(1).Id);
    }

    [Fact]
    public void ScopeDisposalFailureClearsTheUnloadingMarker()
    {
        using var fixture = new Fixture(() => throw new InvalidOperationException("scope"));
        fixture.Create(1);
        Assert.Throws<InvalidOperationException>(() => fixture.Manager.UnloadRoom(1));
        // No stale scope remains, even when disposing its service failed.
        Assert.Equal(1u, fixture.Create(1).Id);
        var marker = (HashSet<uint>)typeof(RoomManager).GetField("_unloadingRooms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fixture.Manager)!;
        Assert.Empty(marker);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, nameof(IRoomUserStore.SavePet))]
    [InlineData(true, nameof(IRoomUserStore.SavePet))]
    [InlineData(false, nameof(IRoomUserStore.UpdateUserCount))]
    [InlineData(true, nameof(IRoomUserStore.UpdateUserCount))]
    public void UserPersistenceFailureStillFlushesWallItemsAndReleasesScope(bool v2, string? failingWrite)
    {
        var calls = new List<string>();
        var users = new TeardownUserStore(failingWrite, calls);
        var items = new TeardownItemStore(calls);
        using var fixture = new Fixture(() => calls.Add("scope"), room =>
        {
            var map = new Gamemap(room, new RoomModel("test", 0, 0, 0, 0, "000\r000\r000", 0, 0, false), TestLogging.Navigation);
            Set(room, "_gamemap", map);
            Set(room, "_roomItemHandling", new RoomItemHandling(room, items));
            Set(room, "_roomUserManager", new RoomUserManager(room, users, TimeProvider.System));
            TestRoomUserSnapshots.Install(room);
            if (v2)
                typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(map, new RoomNavigation(room, map.StaticModel, new() { Engine = PathfindingEngine.V2 }, TestLogging.Navigation));
            map.GenerateMaps();
        });
        var room = fixture.Create(1);
        Assert.Equal(v2, room.UsesV2Movement);
        var handler = room.GetRoomItemHandler();
        var wall = new Item
        {
            Id = 50, RoomId = room.Id, UserId = 7, OwnerId = 7,
            Definition = new ItemDefinition { Type = ItemType.Wall, ItemName = "poster" },
            WallCoordinates = ":w=1,1 l=1,1 l"
        };
        wall.ExtraData = FurniExtraData.Load(wall.Definition, "initial", true);
        handler.LoadFurniture([wall]);
        wall.WallCoordinates = ":w=2,2 l=3,4 r";
        wall.LegacyDataString = "changed";
        handler.UpdateItem(wall);
        var speeches = new List<RandomSpeech>();
        var bot = new RoomBot(20, room.Id, "pet", "freeroam", "pet", "", "", 1, 1, 0, 0,
            0, 0, 0, 0, ref speeches, "M", 0, 7, false, 0, false, 0);
        var pet = new Pet(20, 7, room.Id, "pet", 0, "0", "FFFFFF", 1, 80, 90, 0,
            DateTimeOffset.UnixEpoch, 1, 1, 0, 0, 0, 1, -1, "-1", "owner")
        {
            DbState = PetDatabaseUpdateState.NeedsUpdate
        };
        var roomUsers = room.GetRoomUserManager();
        roomUsers.DeployBot(bot, pet);
        Assert.Same(wall, handler.GetItem(wall.Id));
        Assert.Empty(calls);

        var failure = Record.Exception(() => fixture.Manager.UnloadRoom(room.Id));

        Assert.True(items.Saved.Count == 1, $"Pending wall item was not flushed. Unload failure: {failure}");
        Assert.Equal(new RoomItemSave(50, 0, 0, 0, 0, "changed", ":w=2,2 l=3,4 r", true), items.Saved[0]);
        Assert.Null(failure);
        Assert.Equal(new[] { nameof(IRoomUserStore.SavePet), nameof(IRoomUserStore.UpdateUserCount), "SaveMoved", "scope" }, calls);
        Assert.Empty(handler.GetWallAndFloor);
        Assert.Equal((0, 0, 0), (roomUsers.UserCount, roomUsers.PetCount, room.UsersNow));
        Assert.False(fixture.Manager.TryGetRoom(room.Id, out _));
        Assert.Null(room.GetRoomUserManager());
        Assert.Null(room.GetRoomItemHandler());
        Assert.Equal(1, fixture.Disposed);
        fixture.Manager.UnloadRoom(room.Id);
        Assert.Equal(1, fixture.Disposed);
        Assert.Single(items.Saved);
        var marker = (HashSet<uint>)typeof(RoomManager).GetField("_unloadingRooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Manager)!;
        Assert.Empty(marker);
        Assert.NotSame(room, fixture.Create(room.Id));
    }

    private sealed class TeardownUserStore(string? failingWrite, List<string> calls) : IRoomUserStore
    {
        public void SavePet(RoomPetSave pet) => Write(nameof(SavePet));
        public void UpdateUserCount(uint roomId, int count) => Write(nameof(UpdateUserCount));
        private void Write(string method)
        {
            calls.Add(method);
            if (method == failingWrite) throw new InvalidOperationException("teardown " + method);
        }
        public void SaveBot(RoomBotSave bot) => throw new NotSupportedException();
        public void RecordExit(uint roomId, int userId, DateTimeOffset exitedAt, int usersNow) => throw new NotSupportedException();
    }

    private sealed class TeardownItemStore(List<string> calls) : IRoomItemStore
    {
        public List<RoomItemSave> Saved { get; } = [];
        public void SaveMoved(IReadOnlyList<RoomItemSave> items)
        {
            calls.Add("SaveMoved");
            Saved.AddRange(items);
        }
        public void AssignOwner(uint itemId, int userId) => throw new NotSupportedException();
        public void ClearRoom(uint itemId) => throw new NotSupportedException();
        public void SaveWallPosition(uint itemId, string wallPosition) => throw new NotSupportedException();
        public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation) => throw new NotSupportedException();
        public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) => throw new NotSupportedException();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ScopedRoomFactory _factory;
        public int Disposed;
        public RoomManager Manager { get; }
        public Fixture(Action? dispose = null, Action<Room>? initialize = null)
        {
            var services = new ServiceCollection();
            services.AddScoped<Probe>(_ => new Probe(() => { Disposed++; dispose?.Invoke(); }));
            services.AddScoped<IRoomComponent>(provider => new MinimalComponent(provider.GetRequiredService<Probe>(), initialize));
            _provider = services.BuildServiceProvider();
            _factory = new ScopedRoomFactory(_provider.GetRequiredService<IServiceScopeFactory>(), TestLogging.Navigation, TestLogging.Factory);
            Manager = new RoomManager(NullLogger<RoomManager>.Instance, null!, null!, TimeProvider.System, _factory);
        }
        public Room Create(uint id)
        {
            var room = _factory.Create(new RoomData { Id = id });
            var rooms = (ConcurrentDictionary<uint, Room>)typeof(RoomManager).GetField("_rooms", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Manager)!;
            Assert.True(rooms.TryAdd(id, room));
            return room;
        }
        public void Dispose()
        {
            // Scope failures are deliberately exercised; each lease was already removed.
            try { _factory.Dispose(); } catch (InvalidOperationException) { }
            _provider.Dispose();
        }
    }
    private sealed class Probe(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    private sealed class MinimalComponent(Probe probe, Action<Room>? initialize) : IRoomComponent
    {
        public void Initiate(Room room)
        {
            _ = probe;
            room.MutedUsers = [];
            room.UsersWithRights = [];
            room.WordFilterList = [];
            Set(room, "_tents", new Dictionary<uint, List<RoomUser>>());
            Set(room, "_wiredComponent", new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance));
            initialize?.Invoke(room);
        }
        public void Initiated() { }
    }
    private static void Set(Room room, string field, object? value) =>
        typeof(Room).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, value);
}
