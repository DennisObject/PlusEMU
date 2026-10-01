using Microsoft.Extensions.Options;
using NetCoreServer;
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
    public NitroServer(IOptions<NitroServerConfiguration> options, NitroClientFactory clientFactory, IPacketManager packetManager) : base(options, clientFactory, packetManager) { }
}


public class NitroClientFactory : IGameClientFactory<WsSessionProxy, WsServer>
{
    private readonly FlashPacketFactory _packetFactory;
    private readonly IRevisionsCache _revisionsCache;
    private readonly IGameClientManager _clientManager;

    public NitroClientFactory(FlashPacketFactory packetFactory, IRevisionsCache revisionsCache, IGameClientManager clientManager)
    {
        _packetFactory = packetFactory;
        _revisionsCache = revisionsCache;
        _clientManager = clientManager;
    }

    public WsSessionProxy Create(WsServer server)
    {
        var flashClient = new FlashGameClient((NitroServer)server, _packetFactory)
            { Revision = _revisionsCache.InternalRevision };
        var wsSession = new WsSessionProxy((NitroServer)server, flashClient, _clientManager);
        return wsSession;
    }
}
