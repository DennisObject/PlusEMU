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

    // True only when the supplied args have a pending operation that will raise Completed.
    internal Func<SocketAsyncEventArgs, bool> SendCallback { get; set; }
    internal Action? DisconnectRequested { get; set; }

    public Guid Id { get; set; }


    public void Disconnect() => DisconnectRequested?.Invoke();

    protected GameClient(IGameServer server, IPacketFactory packetFactory)
    {
        _packetFactory = packetFactory;
        _server = server;
    }

    internal event Action? CameraContextEnded;
    internal void EndCameraContext() => CameraContextEnded?.Invoke();

    internal void OnDisconnected()
    {
        IsAuthenticated = false;
        EndCameraContext();
        _habbo?.OnDisconnect();
    }

    internal abstract (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer);
    internal virtual async void OnReceived(byte[] buffer, long offset, long size)
    {
        if (size > int.MaxValue) throw new InvalidOperationException("");
        // The transport reuses its receive buffer as soon as this method yields.
        var received = buffer.AsSpan((int)offset, (int)size).ToArray();
        await _receiveLock.WaitAsync();
        try
        {
            await using var stream = PlusMemoryStream.GetStream(received);
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
                    // TODO @80O: Add logging when ILogger interface has been implemented
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
        IsAuthenticated = true;
    }

    public void Send(IServerPacket composer)
    {
        var outgoingMessageId = Revision.InternalIdToOutgoingIdMapping[composer.MessageId];
        SendEncoded(EncodePacket(composer, outgoingMessageId));
        LogPacket(composer, outgoingMessageId);
    }

    // Encoding belongs to this broadcast only: composers can reference mutable room state.
    internal static void SendBroadcast(IServerPacket composer, IEnumerable<GameClient> clients)
    {
        var encodedPackets = new Dictionary<(Revision, IPacketFactory, Type, uint), byte[]>();
        foreach (var client in clients)
        {
            var outgoingMessageId = client.Revision.InternalIdToOutgoingIdMapping[composer.MessageId];
            var key = (client.Revision, client._packetFactory, client.GetType(), outgoingMessageId);
            if (!encodedPackets.TryGetValue(key, out var buffer))
            {
                buffer = client.EncodePacket(composer, outgoingMessageId);
                encodedPackets.Add(key, buffer);
            }
            client.SendEncoded(buffer);
            client.LogPacket(composer, outgoingMessageId);
        }
    }

    private byte[] EncodePacket(IServerPacket composer, uint outgoingMessageId)
    {
        using var stream = PlusMemoryStream.GetStream();
        var packet = _packetFactory.CreateOutgoingPacket(stream);
        composer.Compose(packet);
        var memory = stream.GetBuffer().AsMemory(0, (int)stream.Length);
        CreateHeader(memory, outgoingMessageId);
        // Socket.SendAsync can outlive this stream; never hand its pooled buffer to a send.
        return memory.ToArray();
    }

    private void SendEncoded(byte[] buffer)
    {
        var args = new SocketAsyncEventArgs();
        args.SetBuffer(buffer.AsMemory());
        args.Completed += static (_, completed) => completed.Dispose();
        try
        {
            if (!SendCallback(args))
                args.Dispose();
        }
        catch
        {
            args.Dispose();
            throw;
        }
    }

    private void LogPacket(IServerPacket composer, uint outgoingMessageId)
    {
        if (Log.IsDebugEnabled)
            Log.Debug($"Send Packet: {composer.GetType().Name} (EmuId: {composer.MessageId}, ClientId: {outgoingMessageId})");
    }

    public abstract void CreateHeader(Memory<byte> memory, uint messageId);
}
