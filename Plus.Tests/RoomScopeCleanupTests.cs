using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

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

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly ScopedRoomFactory _factory;
        public int Disposed;
        public RoomManager Manager { get; }
        public Fixture(Action? dispose = null)
        {
            var services = new ServiceCollection();
            services.AddScoped<Probe>(_ => new Probe(() => { Disposed++; dispose?.Invoke(); }));
            services.AddScoped<IRoomComponent, MinimalComponent>();
            _provider = services.BuildServiceProvider();
            _factory = new ScopedRoomFactory(_provider.GetRequiredService<IServiceScopeFactory>());
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
    private sealed class MinimalComponent(Probe probe) : IRoomComponent
    {
        public void Initiate(Room room)
        {
            _ = probe;
            room.MutedUsers = [];
            room.UsersWithRights = [];
            room.WordFilterList = [];
            Set(room, "_tents", new Dictionary<uint, List<RoomUser>>());
            Set(room, "_wiredComponent", new WiredComponent(room));
        }
        public void Initiated() { }
    }
    private static void Set(Room room, string field, object? value) =>
        typeof(Room).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, value);
}
