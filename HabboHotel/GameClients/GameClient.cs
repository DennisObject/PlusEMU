using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
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
    private readonly object _sendLock = new();
    private readonly ILogger<GameClient> _logger;
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private Habbo? _habbo;
    private readonly object _lifecycle = new();
    private readonly CancellationTokenSource _closed = new();

    public RecyclableMemoryStream? _incompleteStream;
    public Arc4? Rc4Client { get; set; }
    private Arc4? _outgoingRc4;
    protected virtual bool SupportsLegacyCrypto => false;

    public bool IsAuthenticated { get; set; } = false;

    private string _machineId = string.Empty;

    [Obsolete("Will be removed")]
    public string MachineId
    {
        get => _machineId;
        set => _machineId = value;
    }

    // The handshake records the machine id without going through the deprecated property.
    public void RecordMachineId(string machineId) => _machineId = machineId;

    private int _pingCount;

    [Obsolete("Will be removed")]
    public int PingCount
    {
        get => _pingCount;
        set => _pingCount = value;
    }

    // A pong clears the ping counter without going through the deprecated property.
    public void ResetPingCount() => _pingCount = 0;

    // The factory sets the internal revision before ClientHello replaces it; a client built without one cannot speak the protocol.
    public Revision? Revision { get; set; }
    private int _revisionSelectionLogged;

    internal bool TryRecordRevisionSelection() => Interlocked.Exchange(ref _revisionSelectionLogged, 1) == 0;

    // True only when the supplied args have a pending operation that will raise Completed; unset until a transport attaches.
    internal Func<SocketAsyncEventArgs, bool>? SendCallback { get; set; }
    internal Action? DisconnectRequested { get; set; }

    public Guid Id { get; set; }


    private Revision RequiredRevision => Revision ?? throw new InvalidOperationException("The client has no packet revision.");

    public void Disconnect()
    {
        Close();
        DisconnectRequested?.Invoke();
    }

    /// <summary>Cancelled once the connection is closing, including after a packet timeout; session work should stop.</summary>
    internal CancellationToken Closed => _closed.Token;

    protected GameClient(IGameServer server, IPacketFactory packetFactory, ILogger<GameClient> logger)
    {
        _packetFactory = packetFactory;
        _server = server;
        _logger = logger;
    }


    internal event Action? CameraContextEnded;
    internal void EndCameraContext() => CameraContextEnded?.Invoke();

    /// <summary>
    /// NetCoreServer can raise a disconnect while it still holds this session's send lock, and logout takes room
    /// and Wired locks whose holders may be sending to this session. Clean up off the transport thread.
    /// </summary>
    internal void OnTransportDisconnected()
    {
        Close();
        ThreadPool.UnsafeQueueUserWorkItem(static client => client.OnDisconnected(), this, false);
    }

    internal void OnDisconnected()
    {
        Habbo? habbo;

        lock (_lifecycle) {
            Close();
            habbo = _habbo;
        }

        IsAuthenticated = false;
        EndCameraContext();

        try {
            habbo?.OnDisconnect();
        }
        catch (Exception exception) {
            _logger.LogError(exception, "Failed to clean up disconnected user {UserId}", habbo?.Id);
        }
    }

    /// <summary>
    /// Attaches a logged-in Habbo and registers the session as one step, unless the connection already closed.
    /// A close after this point finds the Habbo and logs it out.
    /// </summary>
    internal bool TryAttach(Habbo habbo, Action register)
    {
        lock (_lifecycle) {
            if (_closed.IsCancellationRequested) {
                return false;
            }

            SetHabbo(habbo);
            register();

            return true;
        }
    }

    private void Close()
    {
        lock (_lifecycle) {
            if (!_closed.IsCancellationRequested) {
                _closed.Cancel();
            }
        }
    }

    internal abstract (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer);
    internal virtual async void OnReceived(byte[] buffer, long offset, long size)
    {
        if (size > int.MaxValue) {
            throw new InvalidOperationException("");
        }

        // The transport reuses its receive buffer as soon as this method yields.
        var received = buffer.AsSpan((int)offset, (int)size).ToArray();
        await _receiveLock.WaitAsync();

        try {
            if (Closed.IsCancellationRequested) {
                _incompleteStream?.Dispose();
                _incompleteStream = null;

                return;
            }

            var decrypted = SupportsLegacyCrypto && Rc4Client != null;

            if (decrypted) {
                Rc4Client!.Transform(received);
            }

            await using var stream = PlusMemoryStream.GetStream(received);
            var memory = stream.GetBuffer().AsMemory().Slice(0, (int)stream.Length);

            if (_incompleteStream != null) {
                _incompleteStream.Position = _incompleteStream.Length;
                _incompleteStream.Write(memory.Span);
                memory = _incompleteStream.GetBuffer().AsMemory().Slice(0, (int)_incompleteStream.Length);
            }

            while (memory.Length > 0 && !Closed.IsCancellationRequested) {
                var (complete, malformed, messageId, headerLength, length) = GetMessageIdAndPacketLength(memory);

                if (malformed) {
                    Disconnect();
                    _incompleteStream?.Dispose();
                    _incompleteStream = null;

                    return;
                }

                if (!complete) {
                    break;
                }

                try {
                    if (RequiredRevision.IncomingIdToInternalIdMapping.TryGetValue(messageId, out var internalMessageId)) {
                        await using var packetStream = PlusMemoryStream.GetStream(memory.Slice(headerLength, length).Span);
                        await _server.PacketReceived(this, internalMessageId, _packetFactory.CreateIncomingPacket(packetStream));
                    }
                    else {
                        // TODO @80O: Add logging unknown packet received.
                    }
                }
                catch (Exception e) {
                    _logger.LogError(e, "Error handling packet {MessageId}", messageId);
                }

                memory = memory.Slice(headerLength + length);

                if (!decrypted && SupportsLegacyCrypto && Rc4Client != null && !memory.IsEmpty) {
                    Rc4Client.Transform(memory.Span);
                    decrypted = true;
                }
            }

            if (memory.Length == 0 || Closed.IsCancellationRequested) {
                _incompleteStream?.Dispose();
                _incompleteStream = null;
            }
            else {
                var tail = PlusMemoryStream.GetStream(memory.Span);
                _incompleteStream?.Dispose();
                _incompleteStream = tail;
            }
        }
        finally {
            _receiveLock.Release();
        }
    }

    public Habbo GetHabbo() => _habbo!;

    public void SetHabbo(Habbo habbo)
    {
        if (_habbo != null) {
            throw new InvalidOperationException();
        }

        _habbo = habbo;
        IsAuthenticated = true;
    }

    public void Send(IServerPacket composer)
    {
        var outgoingMessageId = RequiredRevision.InternalIdToOutgoingIdMapping[composer.MessageId];
        var encoded = EncodePacket(composer, outgoingMessageId);

        if (encoded == null) {
            return;
        }

        SendEncoded(encoded);
        LogPacket(composer, outgoingMessageId);
    }

    // Encoding belongs to this broadcast only: composers can reference mutable room state.
    internal static void SendBroadcast(IServerPacket composer, IEnumerable<GameClient> clients, Func<GameClient, bool>? canSend = null)
    {
        var encodedPackets = new Dictionary<(Revision, IPacketFactory, Type, uint), byte[]>();

        foreach (var client in clients) {
            // A client without a revision cannot receive packets; it is skipped rather than aborting everyone else's copy.
            if (client.Revision is not { } revision) {
                continue;
            }

            var outgoingMessageId = revision.InternalIdToOutgoingIdMapping[composer.MessageId];
            var key = (revision, client._packetFactory, client.GetType(), outgoingMessageId);
            byte[] buffer;

            if (client._server.HasOutgoingPacketInjectors(composer.MessageId) || !encodedPackets.TryGetValue(key, out buffer!)) {
                buffer = client.EncodePacket(composer, outgoingMessageId)!;

                if (buffer == null) {
                    continue;
                }

                if (!client._server.HasOutgoingPacketInjectors(composer.MessageId)) {
                    encodedPackets.Add(key, buffer);
                }
            }

            client.SendEncoded(buffer, canSend == null ? null : () => canSend(client));
            client.LogPacket(composer, outgoingMessageId);
        }
    }

    private byte[]? EncodePacket(IServerPacket composer, uint outgoingMessageId)
    {
        using var stream = PlusMemoryStream.GetStream();
        var packet = _packetFactory.CreateOutgoingPacket(stream);
        packet.MessageId = checked((int)composer.MessageId);
        composer.Compose(packet);

        if (!_server.ModifyOutgoingPacket(this, packet)) {
            return null;
        }

        var memory = stream.GetBuffer().AsMemory(0, (int)stream.Length);
        CreateHeader(memory, outgoingMessageId);

        // Socket.SendAsync can outlive this stream; never hand its pooled buffer to a send.
        return memory.ToArray();
    }

    public void ActivateLegacyCrypto(byte[] key)
    {
        if (!SupportsLegacyCrypto) {
            return;
        }

        lock (_sendLock) {
            Rc4Client = new Arc4(key);
            _outgoingRc4 = new Arc4(key);
        }
    }

    private void SendEncoded(byte[] buffer, Func<bool>? canSend = null)
    {
        if (!SupportsLegacyCrypto || _outgoingRc4 == null) {
            SendEncodedCore(buffer, canSend);

            return;
        }

        lock (_sendLock) {
            SendEncodedCore(buffer, canSend);
        }
    }

    private void SendEncodedCore(byte[] buffer, Func<bool>? canSend)
    {
        var args = new SocketAsyncEventArgs();
        args.SetBuffer(buffer.AsMemory());
        args.Completed += static (_, completed) => completed.Dispose();

        try {
            // Visit-scoped packets can expire during encoding or a prior recipient's synchronous send.
            // Recheck at the transport boundary, without holding a room/network lock across the callback.
            if (canSend != null && !canSend()) {
                args.Dispose();

                return;
            }

            if (SupportsLegacyCrypto && _outgoingRc4 != null) {
                buffer = buffer.ToArray();
                _outgoingRc4.Transform(buffer);
                args.SetBuffer(buffer.AsMemory());
            }

            // Without an attached transport nothing is pending, so the args are released like a completed send.
            if (SendCallback is not { } send || !send(args)) {
                args.Dispose();
            }
        }
        catch {
            args.Dispose();
            throw;
        }
    }

    private void LogPacket(IServerPacket composer, uint outgoingMessageId)
    {
        if (_logger.IsEnabled(LogLevel.Debug)) {
            _logger.LogDebug("Send Packet: {PacketType} (EmuId: {EmulatorId}, ClientId: {ClientId})", composer.GetType().Name, composer.MessageId, outgoingMessageId);
        }
    }

    public abstract void CreateHeader(Memory<byte> memory, uint messageId);
}
