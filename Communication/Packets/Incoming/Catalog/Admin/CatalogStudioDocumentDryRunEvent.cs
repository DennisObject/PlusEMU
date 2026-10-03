using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

public class CatalogStudioDocumentDryRunEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogStudioDocumentDryRunEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    // The document itself is never read: only the operation id and format are needed to answer.
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        string operationId = packet.ReadString();
        packet.ReadInt(); // draft version
        packet.ReadInt(); // expected revision
        string format = packet.ReadString();
        var (code, message, revision) = CatalogStudioUnsupported.Answer(_catalogAdmin, session, "Catalog import");
        session.Send(new CatalogStudioDocumentResultComposer(operationId, code, message, revision, format));
        return Task.CompletedTask;
    }
}
