using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Handshake;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public class PongEventTests
{
    [Fact]
    public async Task PongClearsThePingCounter()
    {
        var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient);
        var counter = typeof(GameClient).GetField("_pingCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        counter.SetValue(client, 3);

        await new PongEvent().Parse(client, HabbiconTestSupport.Incoming());

        Assert.Equal(0, counter.GetValue(client));
    }
}
