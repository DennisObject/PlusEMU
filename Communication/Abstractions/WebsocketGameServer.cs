using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using NetCoreServer;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.HabboHotel.GameClients;
using Microsoft.Extensions.Logging;

namespace Plus.Communication.Abstractions;

public abstract class WebsocketGameServer<TGameServerOptions> : WsServer, IGameServer
    where TGameServerOptions : class, IGameServerOptions
{
    private readonly IGameClientFactory<WsSessionProxy, WsServer> _clientFactory;
    private readonly IPacketManager _packetManager;
    private readonly IReadOnlyDictionary<uint, IIncomingPacketInjector[]> _incomingInjectors;
    private readonly IReadOnlyDictionary<uint, IOutgoingPacketInjector[]> _outgoingInjectors;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, WsSessionProxy> _connectedClients = new();

    protected WebsocketGameServer(IOptions<TGameServerOptions> options,
        IGameClientFactory<WsSessionProxy, WsServer> clientFactory,
        IPacketManager packetManager,
        IEnumerable<IIncomingPacketInjector> incomingInjectors,
        IEnumerable<IOutgoingPacketInjector> outgoingInjectors, ILogger logger) : base(options.Value.Hostname,
        options.Value.Port)
    {
        _clientFactory = clientFactory;
        _packetManager = packetManager;
        _logger = logger;
        _incomingInjectors = incomingInjectors.GroupBy(x => x.MessageId).ToDictionary(x => x.Key, x => x.ToArray());
        _outgoingInjectors = outgoingInjectors.GroupBy(x => x.MessageId).ToDictionary(x => x.Key, x => x.ToArray());
    }

    protected override WsSession CreateSession() => _clientFactory.Create(this);

    protected override void OnConnected(TcpSession session)
    {
        if (session is not WsSessionProxy gameClient)
        {
            session.Disconnect();
            //_logger.LogWarning("Expected {TGameClient} to be connected. Got {type}", typeof(TGameClient), session.GetType());
            return;
        }

        if (!_connectedClients.TryAdd(gameClient.Id, gameClient))
        {
            //_logger.LogWarning("Failed to cache client. {id} {ip}", gameClient.Id, gameClient.Socket.RemoteEndPoint?.ToString());
            gameClient.Disconnect();
        }
    }

    protected override void OnDisconnected(TcpSession session)
    {
        _connectedClients.TryRemove(session.Id, out _);
    }


    public Task PacketReceived(GameClient client, uint messageId, IIncomingPacket packet)
    {
        packet.MessageId = messageId;
        if (!InvokeInjectors(_incomingInjectors, messageId, injector => injector.ModifyIncomingPacket(this, client, packet)))
            return Task.CompletedTask;
        packet.Stream.Position = 0;
        return _packetManager.TryExecutePacket(client, messageId, packet);
    }

    public bool ModifyOutgoingPacket(GameClient client, IOutgoingPacket packet) =>
        InvokeInjectors(_outgoingInjectors, (uint)packet.MessageId, injector => injector.ModifyOutgoingPacket(this, client, packet));

    public bool HasOutgoingPacketInjectors(uint messageId) => _outgoingInjectors.ContainsKey(messageId);

    private bool InvokeInjectors<T>(IReadOnlyDictionary<uint, T[]> injectors, uint messageId, Action<T> invoke)
    {
        if (!injectors.TryGetValue(messageId, out var matches)) return true;
        foreach (var injector in matches)
        {
            try { invoke(injector); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Packet injector {InjectorType} failed for message {MessageId}; packet aborted", injector!.GetType().Name, messageId);
                return false;
            }
        }
        return true;
    }
}
