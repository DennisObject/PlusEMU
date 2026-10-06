using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog.Admin;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class CatalogStudioUndoEvent : IPacketEvent
{
    private readonly ICatalogAdminService _catalogAdmin;

    public CatalogStudioUndoEvent(ICatalogAdminService catalogAdmin) => _catalogAdmin = catalogAdmin;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        string operationId = packet.ReadString();
        int draftVersionId = packet.ReadInt(), expectedRevision = packet.ReadInt(), groupId = packet.ReadInt();
        var envelope = new CatalogAdminEnvelope(CatalogAdminTypes.Normal, draftVersionId, expectedRevision, string.Empty, $"Undo #{groupId}", operationId);
        var outcome = _catalogAdmin.Undo(session.GetHabbo(), envelope, groupId);
        IReadOnlyList<(string, int)> changed = outcome.Success ? [(outcome.EntityType, outcome.EntityId)] : [];
        session.Send(new CatalogStudioOperationComposer(operationId, outcome.Success, outcome.Code, outcome.Message, outcome.Revision, changed));

        return Task.CompletedTask;
    }
}
