using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class FurniEditorSearchEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;

    public FurniEditorSearchEvent(IFurniEditorService furniEditor) => _furniEditor = furniEditor;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        string query = packet.ReadString(), type = packet.ReadString();
        int page = packet.ReadInt();
        string sortField = packet.ReadString(), sortDirection = packet.ReadString();
        FurniEditorResponder.Read(session, () => new FurniEditorSearchResultComposer(_furniEditor.Search(session.GetHabbo(), query, type, page, sortField, sortDirection)));

        return Task.CompletedTask;
    }
}
