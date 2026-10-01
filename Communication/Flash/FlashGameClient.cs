using System.Net.Sockets;
using System.Text;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Flash;

public class FlashGameClient : GameClient
{
    private bool _hasReceivedPolicy;
    private static byte[] XmlPolicy = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?>\r\n" +
                                                             "<!DOCTYPE cross-domain-policy SYSTEM \"/xml/dtds/cross-domain-policy.dtd\">\r\n" +
                                                             "<cross-domain-policy>\r\n" +
                                                             "<allow-access-from domain=\"*\" to-ports=\"1-31111\" />\r\n" +
                                                             "</cross-domain-policy>\x0");
    public FlashGameClient(IGameServer server, IPacketFactory packetFactory) : base(server, packetFactory)
    {
    }

    public static int DecodeInt32(ReadOnlyMemory<byte> v) => (v.Span[0] << 24) | (v.Span[1] << 16) | (v.Span[2] << 8) | v.Span[3];
    public static int DecodeInt16(ReadOnlyMemory<byte> v) => (v.Span[0] << 8) | v.Span[1];

    public static void EncodeInt32(Memory<byte> memory, int value, int offset)
    {
        var span = memory.Span;
        span[offset + 0] = (byte)((value >> 24) & 0xFF);
        span[offset + 1] = (byte)((value >> 16) & 0xFF);
        span[offset + 2] = (byte)((value >> 8) & 0xFF);
        span[offset + 3] = (byte)((value >> 0) & 0xFF);
    }
    public static void EncodeInt16(Memory<byte> memory, short value, int offset)
    {
        var span = memory.Span;
        span[offset + 0] = (byte)((value >> 8) & 0xFF);
        span[offset + 1] = (byte)((value >> 0) & 0xFF);
    }

    internal override void OnReceived(byte[] buffer, long offset, long size)
    {
        if (!_hasReceivedPolicy && buffer[offset] == (byte)'<')
        {
            _hasReceivedPolicy = true;
            SendPolicy();
            Disconnect();
            return;
        }

        base.OnReceived(buffer, offset, size);
    }

    private void SendPolicy()
    {
        var args = new SocketAsyncEventArgs();
        args.SetBuffer(XmlPolicy);
        SendCallback(args);
    }

    public override void CreateHeader(Memory<byte> memory, uint messageId)
    {
        EncodeInt32(memory, memory.Length - 4, 0);
        EncodeInt16(memory, (short)messageId, 4);
    }

    internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer)
    {
        if (buffer.Length < 4) return default;
        var declaredLength = DecodeInt32(buffer);
        if (declaredLength < 2 || declaredLength > 500000) return (false, true, 0, 0, 0);
        if (buffer.Length < 4 + declaredLength) return default;
        var messageId = (uint)DecodeInt16(buffer.Slice(4));
        return (true, false, messageId, 6, declaredLength - 2);
    }
}