using Plus.Communication.Flash;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Plus.Communication.Encryption.Crypto.Prng;
using Plus.Communication.Packets;
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

    [Fact]
    public void LegacyCryptoDecryptsIncomingFrames()
    {
        var server = new FakeServer();
        var client = Client(server, 1u);
        var key = new byte[] { 1, 2, 3, 4 };
        client.ActivateLegacyCrypto(key);
        var frame = new byte[] { 0, 0, 0, 2, 0, 1 };
        new Arc4(key).Encrypt(ref frame);

        client.OnReceived(frame, 0, frame.Length);

        Assert.Equal(new uint[] { 1 }, server.MessageIds);
    }

    [Fact]
    public void LegacyCryptoEncryptsOutgoingFramesAfterInjection()
    {
        var server = new FakeServer { Modify = packet => packet.WriteByte(7) };
        var client = Client(server);
        client.Revision.InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [10] = 20 };
        var key = new byte[] { 1, 2, 3, 4 };
        client.ActivateLegacyCrypto(key);
        byte[]? sent = null;
        client.SendCallback = args => { sent = args.MemoryBuffer.ToArray(); return false; };

        client.Send(new TestComposer());

        Assert.NotNull(sent);
        new Arc4(key).Decrypt(ref sent!);
        Assert.Equal(new byte[] { 0, 0, 0, 4, 0, 20, 5, 7 }, sent);
    }

    [Fact]
    public void RejectedEncryptedBroadcastDoesNotAdvanceCipher()
    {
        var server = new FakeServer();
        var client = Client(server);
        client.Revision.InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [10] = 20 };
        var key = new byte[] { 1, 2, 3, 4 };
        client.ActivateLegacyCrypto(key);
        byte[]? sent = null;
        client.SendCallback = args => { sent = args.MemoryBuffer.ToArray(); return false; };
        var admitted = false;

        GameClient.SendBroadcast(new TestComposer(), new[] { client, client }, _ => admitted = !admitted);

        Assert.NotNull(sent);
        new Arc4(key).Decrypt(ref sent!);
        Assert.Equal(new byte[] { 0, 0, 0, 3, 0, 20, 5 }, sent);
    }

    [Fact]
    public void PacketReadsDoNotMutateRewoundPayload()
    {
        using var stream = PlusMemoryStream.GetStream(new byte[] { 1, 2, 3, 4 });
        var packet = new FlashIncomingPacket(stream);

        Assert.Equal(0x01020304, packet.ReadInt());
        packet.Stream.Position = 0;
        Assert.Equal(0x01020304, packet.ReadInt());
    }

    [Fact]
    public void CryptoActivatedByHandshakeDecryptsCoalescedRemainder()
    {
        var key = new byte[] { 1, 2, 3, 4 };
        var server = new FakeServer();
        var client = Client(server, 1u, 2u);
        server.Receive = (messageId, _) =>
        {
            if (messageId == 1) client.ActivateLegacyCrypto(key);
        };
        var encrypted = new byte[] { 0, 0, 0, 2, 0, 2 };
        new Arc4(key).Encrypt(ref encrypted);
        var coalesced = new byte[] { 0, 0, 0, 2, 0, 1 }.Concat(encrypted).ToArray();

        client.OnReceived(coalesced, 0, coalesced.Length);

        Assert.Equal(new uint[] { 1, 2 }, server.MessageIds);
    }

    [Fact]
    public void BroadcastInjectorsReceiveIsolatedRecipientPayloads()
    {
        var firstServer = new FakeServer { Modify = packet => packet.WriteByte(7) };
        var secondServer = new FakeServer { Modify = packet => packet.WriteByte(8) };
        var first = Client(firstServer);
        var second = Client(secondServer);
        first.Revision.InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [10] = 20 };
        second.Revision.InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [10] = 20 };
        byte[]? firstBytes = null;
        byte[]? secondBytes = null;
        first.SendCallback = args => { firstBytes = args.MemoryBuffer.ToArray(); return false; };
        second.SendCallback = args => { secondBytes = args.MemoryBuffer.ToArray(); return false; };

        GameClient.SendBroadcast(new TestComposer(), new[] { first, second });

        Assert.Equal(new byte[] { 0, 0, 0, 4, 0, 20, 5, 7 }, firstBytes);
        Assert.Equal(new byte[] { 0, 0, 0, 4, 0, 20, 5, 8 }, secondBytes);
    }

    [Fact]
    public void IncomingFramesHaveIsolatedRecyclableStreams()
    {
        var server = new FakeServer();
        var client = Client(server, 1u, 2u);
        var payloads = new List<byte>();
        server.Receive = (_, packet) =>
        {
            payloads.Add(packet.ReadByte());
            packet.Stream.GetBuffer()[0] = 99;
        };
        var frames = new byte[] { 0, 0, 0, 3, 0, 1, 7, 0, 0, 0, 3, 0, 2, 8 };

        client.OnReceived(frames, 0, frames.Length);

        Assert.Equal(new byte[] { 7, 8 }, payloads);
    }

    [Fact]
    public void RejectedOutgoingInjectionDoesNotTransmitPartialPacket()
    {
        var server = new FakeServer { Modify = packet => packet.WriteByte(7), RejectModification = true };
        var client = Client(server);
        client.Revision.InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [10] = 20 };
        var sends = 0;
        client.SendCallback = _ => { sends++; return false; };

        client.Send(new TestComposer());

        Assert.Equal(0, sends);
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
        public Action<IOutgoingPacket>? Modify { get; init; }
        public Action<uint, IIncomingPacket>? Receive { get; set; }
        public bool RejectModification { get; init; }

        public bool Start() => true;
        public bool Stop() => true;

        public Task PacketReceived(GameClient client, uint messageId, IIncomingPacket packet)
        {
            MessageIds.Add(messageId);
            Receive?.Invoke(messageId, packet);
            return Hold ?? Task.CompletedTask;
        }

        public bool ModifyOutgoingPacket(GameClient client, IOutgoingPacket packet)
        {
            Modify?.Invoke(packet);
            return !RejectModification;
        }
        public bool HasOutgoingPacketInjectors(uint messageId) => Modify != null;
    }


    private sealed class TestComposer : IServerPacket
    {
        public uint MessageId => 10;
        public void Compose(IOutgoingPacket packet) => packet.WriteByte(5);
    }
}
