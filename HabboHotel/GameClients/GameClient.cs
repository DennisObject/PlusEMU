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
    private readonly object _sendLock = new();
    private static readonly ILogger Log = LogManager.GetLogger("Plus.HabboHotel.GameClients.GameClient");
    private Habbo? _habbo;
    private readonly object _lifecycle = new();
    private readonly CancellationTokenSource _closed = new();

    public RecyclableMemoryStream? _incompleteStream;
    public Arc4? Rc4Client { get; set; }
    private Arc4? _outgoingRc4;
    protected virtual bool SupportsLegacyCrypto => false;

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


    public void Disconnect()
    {
        Close();
        DisconnectRequested?.Invoke();
    }

    /// <summary>Cancelled once the connection is closing, including after a packet timeout; session work should stop.</summary>
    internal CancellationToken Closed => _closed.Token;

    protected GameClient(IGameServer server, IPacketFactory packetFactory)
    {
        _packetFactory = packetFactory;
        _server = server;
    }

    internal event Action? CameraContextEnded;
    internal void EndCameraContext() => CameraContextEnded?.Invoke();

    internal void OnDisconnected()
    {
        Habbo? habbo;
        lock (_lifecycle)
        {
            Close();
            habbo = _habbo;
        }
        IsAuthenticated = false;
        EndCameraContext();
        habbo?.OnDisconnect();
    }

    /// <summary>
    /// Attaches a logged-in Habbo and registers the session as one step, unless the connection already closed.
    /// A close after this point finds the Habbo and logs it out.
    /// </summary>
    internal bool TryAttach(Habbo habbo, Action register)
    {
        lock (_lifecycle)
        {
            if (_closed.IsCancellationRequested) return false;
            SetHabbo(habbo);
            register();
            return true;
        }
    }

    private void Close()
    {
        lock (_lifecycle)
        {
            if (!_closed.IsCancellationRequested) _closed.Cancel();
        }
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
            if (SupportsLegacyCrypto && Rc4Client != null) Rc4Client.Decrypt(ref received);
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
                        await using var packetStream = PlusMemoryStream.GetStream(memory.Slice(headerLength, length).Span);
                        await _server.PacketReceived(this, internalMessageId, _packetFactory.CreateIncomingPacket(packetStream));
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
        IsAuthenticated = true;
    }

    public void Send(IServerPacket composer)
    {
        lock (_sendLock)
        {
            var outgoingMessageId = Revision.InternalIdToOutgoingIdMapping[composer.MessageId];
            SendEncoded(EncodePacket(composer, outgoingMessageId));
            LogPacket(composer, outgoingMessageId);
        }
    }

    // Encoding belongs to this broadcast only: composers can reference mutable room state.
    internal static void SendBroadcast(IServerPacket composer, IEnumerable<GameClient> clients, Func<GameClient, bool>? canSend = null)
    {
        foreach (var client in clients)
        {
            lock (client._sendLock)
            {
                var outgoingMessageId = client.Revision.InternalIdToOutgoingIdMapping[composer.MessageId];
                var buffer = client.EncodePacket(composer, outgoingMessageId);
                client.SendEncoded(buffer, canSend == null ? null : () => canSend(client));
                client.LogPacket(composer, outgoingMessageId);
            }
        }
    }

    private byte[] EncodePacket(IServerPacket composer, uint outgoingMessageId)
    {
        using var stream = PlusMemoryStream.GetStream();
        var packet = _packetFactory.CreateOutgoingPacket(stream);
        packet.MessageId = checked((int)composer.MessageId);
        composer.Compose(packet);
        _server.ModifyOutgoingPacket(this, packet);
        var memory = stream.GetBuffer().AsMemory(0, (int)stream.Length);
        CreateHeader(memory, outgoingMessageId);
        // Socket.SendAsync can outlive this stream; never hand its pooled buffer to a send.
        var encoded = memory.ToArray();
        if (SupportsLegacyCrypto && _outgoingRc4 != null) _outgoingRc4.Encrypt(ref encoded);
        return encoded;
    }

    public void ActivateLegacyCrypto(byte[] key)
    {
        if (!SupportsLegacyCrypto) return;
        Rc4Client = new Arc4(key);
        _outgoingRc4 = new Arc4(key);
    }

    private void SendEncoded(byte[] buffer, Func<bool>? canSend = null)
    {
        var args = new SocketAsyncEventArgs();
        args.SetBuffer(buffer.AsMemory());
        args.Completed += static (_, completed) => completed.Dispose();
        try
        {
            // Visit-scoped packets can expire during encoding or a prior recipient's synchronous send.
            // Recheck at the transport boundary, without holding a room/network lock across the callback.
            if (canSend != null && !canSend())
            {
                args.Dispose();
                return;
            }
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
