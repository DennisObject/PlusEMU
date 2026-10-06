using Microsoft.Extensions.Logging;
using Plus.Communication.Attributes;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Handshake;

[NoAuthenticationRequired]
public class ClientHelloEvent : IPacketEvent
{
    private readonly IRevisionsCache _revisionsCache;
    private readonly ILogger _logger;

    public ClientHelloEvent(IRevisionsCache revisionsCache, ILogger<ClientHelloEvent> logger)
    {
        _revisionsCache = revisionsCache;
        _logger = logger;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var build = packet.ReadString();
        var clientType = packet.ReadString();
        var clientPlatform = packet.ReadInt();
        var clientDeviceType = packet.ReadInt();

        if (!_revisionsCache.Revisions.TryGetValue(build, out var revision))
        {
            _logger.LogWarning("Unknown revision connected {revision}.", build);
            session.Disconnect();

            return Task.CompletedTask;
        }

        session.Revision = revision;

        return Task.CompletedTask;
    }
}
