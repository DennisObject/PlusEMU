using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogStudioValidateEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogStudioValidateEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        string operationId = packet.ReadString();
        var (code, message, revision) = CatalogStudioUnsupported.Answer(_catalogAdmin, session, "Catalog validation");
        session.Send(new CatalogStudioValidationComposer(operationId, code, message, revision));
        return Task.CompletedTask;
    }
}
