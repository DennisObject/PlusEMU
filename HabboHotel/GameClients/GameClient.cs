using System.Net.Sockets;
using Microsoft.IO;
using NLog;
using Plus.Communication.Encryption.Crypto.Prng;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Revisions;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.GameClients;

public abstract class GameClient
{
    private readonly IGameServer _server;
    private readonly IPacketFactory _packetFactory;
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private static readonly ILogger Log = LogManager.GetLogger("Plus.HabboHotel.GameClients.GameClient");
    private Habbo? _habbo;

    public RecyclableMemoryStream? _incompleteStream;
    public Arc4? Rc4Client { get; set; }

    public bool IsAuthenticated { get; set; } = false;
    public DateTime TimeConnected { get; set; }

    [Obsolete("Will be removed")]
    public string MachineId { get; set; } = string.Empty;

    [Obsolete("Will be removed")]
    public int PingCount { get; set; }

    public Revision Revision { get; set; }

    internal Func<SocketAsyncEventArgs, bool> SendCallback { get; set; }
    internal Action? DisconnectRequested { get; set; }

    public Guid Id { get; set; }


    public void Disconnect() => DisconnectRequested?.Invoke();

    protected GameClient(IGameServer server, IPacketFactory packetFactory)
    {
        _packetFactory = packetFactory;
        _server = server;
    }

    internal void OnDisconnected() => _habbo?.OnDisconnect();

    internal abstract (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer);
    internal virtual async void OnReceived(byte[] buffer, long offset, long size)
    {
        if (size > int.MaxValue) throw new InvalidOperationException("");
        await _receiveLock.WaitAsync();
        try
        {
            await using var stream = PlusMemoryStream.GetStream(buffer.AsSpan().Slice((int) offset, (int) size));
            var memory = stream.GetBuffer().AsMemory().Slice(0, (int)stream.Length);

            if (_incompleteStream != null)
            {
                _incompleteStream.Position = _incompleteStream.Length;
                _incompleteStream.Write(memory.Span);
                memory = _incompleteStream.GetBuffer().AsMemory().Slice(0, (int)_incompleteStream.Length);
            }

            while (memory.Length > 0)
            {
                var (complete, malformed, messageId, headerLength, length) = GetMessageIdAndPacketLength(memory);
                if (malformed)
                {
                    Disconnect();
                    _incompleteStream?.Dispose();
                    _incompleteStream = null;
                    return;
                }

                if (!complete) break;

                try
                {
                    if (Revision.IncomingIdToInternalIdMapping.TryGetValue(messageId, out var internalMessageId))
                    {
                        await _server.PacketReceived(this, internalMessageId, _packetFactory.CreateIncomingPacket(memory.Slice(headerLength, length)));
                    }
                    else
                    {
                        // TODO @80O: Add logging unknown packet received.
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Error handling packet {messageId}");
                }
                memory = memory.Slice(headerLength + length);
            }

            if (memory.Length == 0)
            {
                _incompleteStream?.Dispose();
                _incompleteStream = null;
            }
            else
            {
                var tail = PlusMemoryStream.GetStream(memory.Span);
                _incompleteStream?.Dispose();
                _incompleteStream = tail;
            }
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    public Habbo GetHabbo() => _habbo!;

    public void SetHabbo(Habbo habbo)
    {
        if (_habbo != null) throw new InvalidOperationException();
        _habbo = habbo;
    }

    public void Send(IServerPacket composer)
    {
        var outgoingMessageId = Revision.InternalIdToOutgoingIdMapping[composer.MessageId];
        var stream = PlusMemoryStream.GetStream();
        stream.Position = 0;
        var packet = _packetFactory.CreateOutgoingPacket(stream);
        composer.Compose(packet);
        var args = new SocketAsyncEventArgs();
        var memory = stream.GetBuffer().AsMemory().Slice(0, (int)stream.Length);
        CreateHeader(memory, outgoingMessageId);
        args.SetBuffer(memory);
        SendCallback(args);
        Log.Debug($"Send Packet: {composer.GetType().Name} (EmuId: {composer.MessageId}, ClientId: {outgoingMessageId})");
        stream.Dispose();
    }

    public abstract void CreateHeader(Memory<byte> memory, uint messageId);
}
