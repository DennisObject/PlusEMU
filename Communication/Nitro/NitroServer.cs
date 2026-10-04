using Microsoft.Extensions.Options;
using NetCoreServer;
using Microsoft.Extensions.Logging;
using Plus.Communication.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Nitro;

public class NitroServerConfiguration : IGameServerOptions
{
    public string Name { get; set; }
    public int Port { get; set; }
    public string Hostname { get; set; }
}

public interface INitroServer : IGameServer
{
}

public class NitroServer : WebsocketGameServer<NitroServerConfiguration>, INitroServer
{
    public NitroServer(IOptions<NitroServerConfiguration> options, NitroClientFactory clientFactory, IPacketManager packetManager,
        IEnumerable<IIncomingPacketInjector> incomingInjectors, IEnumerable<IOutgoingPacketInjector> outgoingInjectors)
        : base(options, clientFactory, packetManager, incomingInjectors, outgoingInjectors) { }
}


public class NitroClientFactory : IGameClientFactory<WsSessionProxy, WsServer>
{
    private readonly FlashPacketFactory _packetFactory;
    private readonly IRevisionsCache _revisionsCache;
    private readonly ILogger<GameClient> _logger;

    public NitroClientFactory(FlashPacketFactory packetFactory, IRevisionsCache revisionsCache, ILogger<GameClient> logger)
    {
        _packetFactory = packetFactory;
        _revisionsCache = revisionsCache;
        _logger = logger;
    }

    public WsSessionProxy Create(WsServer server)
    {
        var flashClient = new FlashGameClient((NitroServer)server, _packetFactory, _logger)
            { Revision = _revisionsCache.InternalRevision };
        var wsSession = new WsSessionProxy((NitroServer)server, flashClient);
        return wsSession;
    }
}
