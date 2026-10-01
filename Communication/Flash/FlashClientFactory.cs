using NetCoreServer;
using Plus.Communication.Abstractions;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Flash;

public class FlashClientFactory : IGameClientFactory<TcpSessionProxy, TcpServer>
{
    private readonly FlashPacketFactory _packetFactory;
    private readonly IRevisionsCache _revisionsCache;
    private readonly IGameClientManager _clientManager;

    public FlashClientFactory(FlashPacketFactory packetFactory, IRevisionsCache revisionsCache, IGameClientManager clientManager)
    {
        _packetFactory = packetFactory;
        _revisionsCache = revisionsCache;
        _clientManager = clientManager;
    }

    public TcpSessionProxy Create(TcpServer server) => new((FlashServer)server, new FlashGameClient((FlashServer)server, _packetFactory) { Revision = _revisionsCache.InternalRevision }, _clientManager);
}
