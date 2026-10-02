using Plus.Communication.Flash;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public class FlashFramingTests
{
    [Fact]
    public void DeclaredLengthOneIsMalformed()
    {
        var client = new FlashGameClient(new FakeServer(), new FlashPacketFactory());
        var result = client.GetMessageIdAndPacketLength(new byte[] { 0, 0, 0, 1, 0, 1 });

        Assert.False(result.Complete);
        Assert.True(result.Malformed);
        Assert.True(result.Length >= 0);
    }

    [Fact]
    public void SplitFrameIsDeliveredOnce()
    {
        var server = new FakeServer();
        var client = Client(server, 1u);
        var frame = new byte[] { 0, 0, 0, 2, 0, 1 };

        client.OnReceived(frame, 0, 4);
        Assert.Equal(0, server.Count);

        client.OnReceived(frame, 4, 2);

        Assert.Equal(1, server.Count);
        Assert.Equal(new uint[] { 1 }, server.MessageIds);
    }

    [Fact]
    public void FramePlusPartialNextFrameIsNotReplayed()
    {
        var server = new FakeServer();
        var client = Client(server, 1u, 2u);
        var first = new byte[] { 0, 0, 0, 2, 0, 1, 0, 0 };
        var second = new byte[] { 0, 2, 0, 2 };

        client.OnReceived(first, 0, first.Length);
        client.OnReceived(second, 0, second.Length);

        Assert.Equal(new uint[] { 1, 2 }, server.MessageIds);
    }

    [Fact]
    public void MalformedFrameDisconnectsWithoutDelivery()
    {
        var server = new FakeServer();
        var client = Client(server, 1u);
        var disconnected = 0;
        client.DisconnectRequested = () => disconnected++;

        client.OnReceived(new byte[] { 0, 0, 0, 1, 0, 1 }, 0, 6);

        Assert.Equal(1, disconnected);
        Assert.Equal(0, server.Count);
    }

    [Fact]
    public async Task ReusedReceiveBufferIsSnapshottedBeforeYield()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new FakeServer { Hold = release.Task };
        var client = Client(server, 1u, 2u);
        var disconnected = 0;
        client.DisconnectRequested = () => disconnected++;
        var shared = new byte[] { 0, 0, 0, 2, 0, 1 };

        client.OnReceived(shared, 0, shared.Length);
        shared[4] = 0;
        shared[5] = 2;
        client.OnReceived(shared, 0, shared.Length);
        Array.Clear(shared);
        release.TrySetResult();

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (server.Count < 2 && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.Equal(0, disconnected);
        Assert.Equal(new uint[] { 1, 2 }, server.MessageIds);
    }

    private static FlashGameClient Client(FakeServer server, params uint[] messageIds)
    {
        var client = new FlashGameClient(server, new FlashPacketFactory())
        {
            Revision = new Revision
            {
                IncomingIdToInternalIdMapping = messageIds.ToDictionary(id => id, id => id)
            },
            DisconnectRequested = () => { }
        };
        return client;
    }

    private sealed class FakeServer : IGameServer
    {
        public Task? Hold { get; init; }
        public int Count => MessageIds.Count;
        public List<uint> MessageIds { get; } = new();

        public bool Start() => true;
        public bool Stop() => true;

        public Task PacketReceived(GameClient client, uint messageId, IIncomingPacket packet)
        {
            MessageIds.Add(messageId);
            return Hold ?? Task.CompletedTask;
        }
    }
}
