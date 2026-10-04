using Microsoft.Extensions.DependencyInjection;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomLifecycleTests
{
    [Fact]
    public void InitiateRunsEveryFirstPhaseBeforeAnySecondPhase()
    {
        var calls = new List<string>();
        var room = new Room(Data(1), [new RecordingComponent("a", calls), new RecordingComponent("b", calls)], TestLogging.Navigation, TestLogging.Logger);

        room.Initiate();

        Assert.Equal(["a:init", "b:init", "a:ready", "b:ready"], calls);
    }

    [Fact]
    public void InitiateRejectsSecondCall()
    {
        var room = new Room(Data(1), Array.Empty<IRoomComponent>(), TestLogging.Navigation, TestLogging.Logger);
        room.Initiate();

        Assert.Throws<InvalidOperationException>(room.Initiate);
    }

    [Fact]
    public void FactoryOwnsIndependentScopesAndDisposesThemWithRooms()
    {
        var services = new ServiceCollection();
        services.AddScoped<Probe>();
        services.AddScoped<IRoomComponent, ScopedProbeComponent>();
        using var provider = services.BuildServiceProvider();
        using var factory = new ScopedRoomFactory(provider.GetRequiredService<IServiceScopeFactory>(), TestLogging.Navigation, TestLogging.Factory);

        var first = factory.Create(Data(1));
        var second = factory.Create(Data(2));
        var firstProbe = Assert.IsType<ScopedProbeComponent>(first.Components.Single()).Probe;
        var secondProbe = Assert.IsType<ScopedProbeComponent>(second.Components.Single()).Probe;

        Assert.NotSame(firstProbe, secondProbe);
        factory.Dispose(first.Id);
        Assert.True(firstProbe.Disposed);
        Assert.False(secondProbe.Disposed);
        factory.Dispose(second.Id);
        Assert.True(secondProbe.Disposed);
    }

    [Fact]
    public void DuplicateRoomDoesNotEvictOriginalScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<Probe>();
        services.AddScoped<IRoomComponent, ScopedProbeComponent>();
        using var provider = services.BuildServiceProvider();
        using var factory = new ScopedRoomFactory(provider.GetRequiredService<IServiceScopeFactory>(), TestLogging.Navigation, TestLogging.Factory);
        var room = factory.Create(Data(1));
        var probe = Assert.IsType<ScopedProbeComponent>(room.Components.Single()).Probe;

        Assert.Throws<InvalidOperationException>(() => factory.Create(Data(1)));
        Assert.False(probe.Disposed);
        factory.Dispose(1);
        Assert.True(probe.Disposed);
    }

    [Fact]
    public void FailedInitiationDoesNotEvictAReplacementScope()
    {
        var services = new ServiceCollection();
        services.AddScoped<Probe>();
        ScopedRoomFactory? factory = null;
        Room? replacement = null;
        var replacing = false;
        services.AddScoped<IRoomComponent>(provider => new ReplacingComponent(provider.GetRequiredService<Probe>(), () =>
        {
            if (replacing) return;
            replacing = true;
            factory!.Dispose(1);
            replacement = factory.Create(Data(1));
            throw new InvalidOperationException("The original initialization failed.");
        }));
        using var provider = services.BuildServiceProvider();
        using var ownedFactory = new ScopedRoomFactory(provider.GetRequiredService<IServiceScopeFactory>());
        factory = ownedFactory;

        Assert.Throws<InvalidOperationException>(() => factory.Create(Data(1)));
        var probe = Assert.IsType<ReplacingComponent>(replacement!.Components.Single()).Probe;
        Assert.False(probe.Disposed);
        factory.Dispose(1);
        Assert.True(probe.Disposed);
    }

    private sealed class ReplacingComponent(Probe probe, Action initiate) : IRoomComponent
    {
        public Probe Probe { get; } = probe;
        public void Initiate(Room room) => initiate();
        public void Initiated() { }
    }

    private static RoomData Data(uint id)
    {
        var data = (RoomData)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RoomData));
        data.Id = id;
        return data;
    }

    private sealed class RecordingComponent(string name, List<string> calls) : IRoomComponent
    {
        public void Initiate(Room room) => calls.Add($"{name}:init");
        public void Initiated() => calls.Add($"{name}:ready");
    }

    private sealed class Probe : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class ScopedProbeComponent(Probe probe) : IRoomComponent
    {
        public Probe Probe { get; } = probe;
        public void Initiate(Room room) { }
        public void Initiated() { }
    }
}
