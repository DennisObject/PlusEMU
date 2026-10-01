using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NetCoreServer;
using Plus.Communication.Flash;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.GameClients.Tests;

public class GameClientManagerTests
{
    [Fact]
    public void TcpProxyTracksPreLoginClientAndReleasesItOnDisconnect()
    {
        var manager = CreateManager();
        var client = CreateClient();
        using var server = new TcpServer(IPAddress.Loopback, 0);
        using var proxy = new TestTcpSessionProxy(server, client, manager);

        Assert.Equal(proxy.Id, client.Id);
        Assert.NotEqual(Guid.Empty, client.Id);
        Assert.False(client.IsAuthenticated);
        Assert.Equal(1, manager.Count);
        Assert.Same(client, Assert.Single(manager.GetClients));
        Assert.True(manager.TryGetClient(proxy.Id, out var trackedClient));
        Assert.Same(client, trackedClient);

        proxy.NotifyDisconnected();

        Assert.Equal(0, manager.Count);
        Assert.Empty(manager.GetClients);
        Assert.False(manager.TryGetClient(proxy.Id, out _));
    }

    [Fact]
    public void WsProxyAssignsSessionIdAndTracksPreLoginClientUntilDisconnect()
    {
        var manager = CreateManager();
        var client = CreateClient();
        using var server = new WsServer(IPAddress.Loopback, 0);
        using var proxy = new TestWsSessionProxy(server, client, manager);

        Assert.Equal(proxy.Id, client.Id);
        Assert.NotEqual(Guid.Empty, client.Id);
        Assert.False(client.IsAuthenticated);
        Assert.Equal(1, manager.Count);
        Assert.Same(client, Assert.Single(manager.GetClients));
        Assert.True(manager.TryGetClient(proxy.Id, out var trackedClient));
        Assert.Same(client, trackedClient);

        proxy.NotifyDisconnected();

        Assert.Equal(0, manager.Count);
        Assert.Empty(manager.GetClients);
        Assert.False(manager.TryGetClient(proxy.Id, out _));
    }

    [Fact]
    public void RegisterClientUpdatesUserIndexesWithoutTrackingConnection()
    {
        var manager = CreateManager();
        var client = CreateClient();

        manager.RegisterClient(client, 42, "Dennis");

        Assert.Same(client, manager.GetClientByUserId(42));
        Assert.Same(client, manager.GetClientByUsername("DENNIS"));
        Assert.Equal(0, manager.Count);
        Assert.Empty(manager.GetClients);
    }

    [Fact]
    public void ReleaseClientRemovesOnlyRequestedConnectionAndLeavesUserIndexes()
    {
        var manager = CreateManager();
        var firstClient = CreateClient();
        firstClient.Id = Guid.NewGuid();
        var secondClient = CreateClient();
        secondClient.Id = Guid.NewGuid();
        manager.TrackClient(firstClient);
        manager.TrackClient(secondClient);
        manager.RegisterClient(firstClient, 42, "Dennis");

        Assert.Equal(2, manager.Count);
        Assert.True(manager.TryGetClient(firstClient.Id, out var trackedClient));
        Assert.Same(firstClient, trackedClient);

        manager.ReleaseClient(firstClient.Id);
        manager.ReleaseClient(firstClient.Id);

        Assert.Equal(1, manager.Count);
        Assert.Same(secondClient, Assert.Single(manager.GetClients));
        Assert.False(manager.TryGetClient(firstClient.Id, out _));
        Assert.True(manager.TryGetClient(secondClient.Id, out trackedClient));
        Assert.Same(secondClient, trackedClient);
        Assert.Same(firstClient, manager.GetClientByUserId(42));
        Assert.Same(firstClient, manager.GetClientByUsername("Dennis"));
    }

    [Fact]
    public void RegisterClientDoesNotAddTrackedConnectionAgain()
    {
        var manager = CreateManager();
        var client = CreateClient();
        client.Id = Guid.NewGuid();
        manager.TrackClient(client);

        manager.RegisterClient(client, 42, "Dennis");
        manager.RegisterClient(client, 42, "Dennis");

        Assert.Equal(1, manager.Count);
        Assert.Same(client, Assert.Single(manager.GetClients));
        Assert.Same(client, manager.GetClientByUserId(42));
    }

    [Fact]
    public void UnregisterClientRemovesUserIndexesWithoutReleasingConnection()
    {
        var manager = CreateManager();
        var client = CreateClient();
        client.Id = Guid.NewGuid();
        manager.TrackClient(client);
        manager.RegisterClient(client, 42, "Dennis");

        manager.UnregisterClient(42, "Dennis");

        Assert.Null(manager.GetClientByUserId(42));
        Assert.Null(manager.GetClientByUsername("Dennis"));
        Assert.Equal(1, manager.Count);
        Assert.Same(client, Assert.Single(manager.GetClients));
    }

    private static GameClientManager CreateManager() => new(null!, NullLogger<GameClientManager>.Instance);

    private static FlashGameClient CreateClient() => new(null!, null!);

    private sealed class TestTcpSessionProxy : TcpSessionProxy
    {
        public TestTcpSessionProxy(TcpServer server, GameClient client, IGameClientManager clientManager) : base(server, client, clientManager) { }

        public void NotifyDisconnected() => OnDisconnected();
    }

    private sealed class TestWsSessionProxy : WsSessionProxy
    {
        public TestWsSessionProxy(WsServer server, GameClient client, IGameClientManager clientManager) : base(server, client, clientManager) { }

        public void NotifyDisconnected() => OnDisconnected();
    }
}
