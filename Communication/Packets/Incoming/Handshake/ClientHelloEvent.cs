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

        if (!build.Equals(_revisionsCache.InternalRevision.Name, StringComparison.Ordinal)) {
            _logger.LogWarning("Unknown revision connected {revision}.", build);
            session.Disconnect();

            return Task.CompletedTask;
        }

        var revision = _revisionsCache.InternalRevision;
        session.Revision = revision;

        if (session.TryRecordRevisionSelection()) {
            _logger.LogInformation("Packet revision selected {revision}.", revision.Name);
        }

        return Task.CompletedTask;
    }
}
