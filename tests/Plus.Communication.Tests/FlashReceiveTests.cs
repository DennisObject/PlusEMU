using System.Buffers.Binary;
using Microsoft.IO;
using Plus.Communication.Flash;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Communication.Tests;

public class FlashReceiveTests : IDisposable
{
    private const ushort WireMessageId = 1;
    private const uint InternalMessageId = 100;
    private readonly RecordingServer _server = new();
    private readonly RecordingPacketFactory _packetFactory = new();
    private readonly FlashGameClient _client;
    private bool _disconnected;

    public FlashReceiveTests()
    {
        _client = new FlashGameClient(_server, _packetFactory)
        {
            Revision = new Revision
            {
                Name = "test",
                IncomingHeaders = new Dictionary<string, uint> { ["test"] = WireMessageId },
                IncomingIdToInternalIdMapping = new Dictionary<uint, uint> { [WireMessageId] = InternalMessageId },
                OutgoingHeaders = new Dictionary<string, uint>(),
                InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>()
            },
            DisconnectRequested = () => _disconnected = true
        };
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(1, 4)]
    [InlineData(0, 4)]
    [InlineData(-1, 4)]
    [InlineData(int.MinValue, 4)]
    [InlineData(500001, 4)]
    [InlineData(500001, 6)]
    [InlineData(int.MaxValue, 6)]
    public void IllegalLengthIsRejectedAndDisconnects(int declaredLength, int bufferLength)
    {
        var buffer = LengthPrefix(declaredLength, bufferLength);
        if (bufferLength >= 6)
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), WireMessageId);

        Assert.False(_client.GetMessageIdAndPacketLength(buffer).Complete);

        Receive(buffer);

        Assert.True(_disconnected);
        Assert.Null(_client._incompleteStream);
        Assert.Empty(_server.Packets);
        Assert.Equal(0, _packetFactory.IncomingPacketsCreated);
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(2, 5)]
    [InlineData(3, 6)]
    [InlineData(500000, 6)]
    public void LegalIncompleteFrameIsKeptWithoutDisconnecting(int declaredLength, int bufferLength)
    {
        var buffer = LengthPrefix(declaredLength, bufferLength);

        Assert.False(_client.GetMessageIdAndPacketLength(buffer).Complete);

        Receive(buffer);

        Assert.False(_disconnected);
        Assert.NotNull(_client._incompleteStream);
        Assert.Equal(buffer, _client._incompleteStream!.ToArray());
        Assert.Empty(_server.Packets);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(500000)]
    public void CompleteLegalFrameHasSixByteHeaderAndDispatchesPayload(int declaredLength)
    {
        var payload = new byte[declaredLength - 2];
        Array.Fill(payload, (byte)42);
        var frame = Frame(payload);

        var result = _client.GetMessageIdAndPacketLength(frame);

        Assert.True(result.Complete);
        Assert.Equal((uint)WireMessageId, result.MessageId);
        Assert.Equal(6, result.HeaderLength);
        Assert.Equal(payload.Length, result.Length);

        Receive(frame);

        AssertPackets(payload);
    }

    [Fact]
    public void TwoConcatenatedFramesDispatchOnceEach()
    {
        Receive(Frame(new byte[] { 11 }).Concat(Frame(new byte[] { 22 })).ToArray());

        AssertPackets(new byte[] { 11 }, new byte[] { 22 });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void SplitFrameDispatchesOnlyWhenCompleteAndDoesNotReplay(int splitAt)
    {
        var frame = Frame(new byte[] { 11, 12 });

        Receive(frame[..splitAt]);

        Assert.False(_disconnected);
        Assert.Empty(_server.Packets);
        Assert.Equal(frame[..splitAt], _client._incompleteStream!.ToArray());

        Receive(frame[splitAt..]);

        AssertPackets(new byte[] { 11, 12 });

        Receive(Frame(new byte[] { 22 }));

        AssertPackets(new byte[] { 11, 12 }, new byte[] { 22 });
    }

    [Fact]
    public void CompleteFrameBeforeSplitFrameIsRemovedFromRemainder()
    {
        var secondFrame = Frame(new byte[] { 22 });
        Receive(Frame(new byte[] { 11 }).Concat(secondFrame[..3]).ToArray());

        Assert.Single(_server.Packets);
        Assert.Equal(new byte[] { 11 }, _server.Packets[0].Payload);
        Assert.Equal(secondFrame[..3], _client._incompleteStream!.ToArray());

        Receive(secondFrame[3..]);

        AssertPackets(new byte[] { 11 }, new byte[] { 22 });
    }

    [Fact]
    public void BufferedFrameCompletingBeforeAnotherSplitFrameIsNotReplayed()
    {
        var firstFrame = Frame(new byte[] { 11 });
        var secondFrame = Frame(new byte[] { 22 });
        Receive(firstFrame[..3]);

        Assert.Empty(_server.Packets);

        Receive(firstFrame[3..].Concat(secondFrame[..3]).ToArray());

        Assert.False(_disconnected);
        Assert.Single(_server.Packets);
        Assert.Equal(new byte[] { 11 }, _server.Packets[0].Payload);
        Assert.Equal(secondFrame[..3], _client._incompleteStream!.ToArray());

        Receive(secondFrame[3..]);

        AssertPackets(new byte[] { 11 }, new byte[] { 22 });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500001)]
    [InlineData(-1)]
    public void SplitIllegalLengthDisconnectsAsSoonAsPrefixIsComplete(int declaredLength)
    {
        var prefix = LengthPrefix(declaredLength, 4);
        Receive(prefix[..2]);

        Assert.False(_disconnected);
        Assert.NotNull(_client._incompleteStream);

        Receive(prefix[2..]);

        Assert.True(_disconnected);
        Assert.Null(_client._incompleteStream);
        Assert.Empty(_server.Packets);
    }

    private void Receive(byte[] buffer) => _client.OnReceived(buffer, 0, buffer.Length);

    private void AssertPackets(params byte[][] payloads)
    {
        Assert.False(_disconnected);
        Assert.Null(_client._incompleteStream);
        Assert.Equal(payloads.Length, _server.Packets.Count);
        Assert.Equal(payloads.Length, _packetFactory.IncomingPacketsCreated);
        for (var i = 0; i < payloads.Length; i++)
        {
            Assert.Equal(InternalMessageId, _server.Packets[i].MessageId);
            Assert.Equal(payloads[i], _server.Packets[i].Payload);
        }
    }

    private static byte[] LengthPrefix(int declaredLength, int bufferLength)
    {
        var buffer = new byte[bufferLength];
        BinaryPrimitives.WriteInt32BigEndian(buffer, declaredLength);
        return buffer;
    }

    private static byte[] Frame(byte[] payload)
    {
        var frame = LengthPrefix(payload.Length + 2, payload.Length + 6);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4), WireMessageId);
        payload.CopyTo(frame, 6);
        return frame;
    }

    public void Dispose() => _client._incompleteStream?.Dispose();

    private sealed class RecordingServer : IGameServer
    {
        public List<(uint MessageId, byte[] Payload)> Packets { get; } = new();
        public bool Start() => true;
        public bool Stop() => true;

        public Task PacketReceived(GameClient client, uint messageId, IIncomingPacket packet)
        {
            Packets.Add((messageId, packet.Buffer.ToArray()));
            // Keep dispatch synchronous so OnReceived finishes before assertions.
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPacketFactory : IPacketFactory
    {
        public int IncomingPacketsCreated { get; private set; }

        public IIncomingPacket CreateIncomingPacket(Memory<byte> buffer)
        {
            IncomingPacketsCreated++;
            return new FlashIncomingPacket { Buffer = buffer };
        }

        public IOutgoingPacket CreateOutgoingPacket(RecyclableMemoryStream stream) => throw new NotSupportedException();
    }
}
