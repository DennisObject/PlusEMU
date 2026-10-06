using Plus.Communication.Flash;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public class GameClientManagerTests
{
    [Fact]
    public void RegisterClient_tracks_the_session_and_unregister_drops_only_that_session()
    {
        var manager = new GameClientManager(null, null);
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory());

        manager.RegisterClient(client, 123, "probe");

        Assert.Equal(1, manager.Count);
        Assert.Contains(client, manager.GetClients);
        Assert.Same(client, manager.GetClientByUserId(123));

        manager.UnregisterClient(client, 123, "probe");

        Assert.Equal(0, manager.Count);
        Assert.DoesNotContain(client, manager.GetClients);
        Assert.Null(manager.GetClientByUserId(123));

        var clientA = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory());
        var clientB = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory());
        clientA.Id = Guid.NewGuid();
        clientB.Id = Guid.NewGuid();

        manager.RegisterClient(clientA, 5, "probe");
        manager.RegisterClient(clientB, 5, "probe");
        manager.UnregisterClient(clientA, 5, "probe");

        Assert.Equal(1, manager.Count);
        Assert.Same(clientB, manager.GetClientByUserId(5));
        Assert.Contains(clientB, manager.GetClients);
    }
}
