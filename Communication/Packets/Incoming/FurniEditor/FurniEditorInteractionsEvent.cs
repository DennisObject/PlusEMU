using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

[RequiresPermission(PermissionKeys.CatalogEdit)]
public class FurniEditorInteractionsEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;

    public FurniEditorInteractionsEvent(IFurniEditorService furniEditor) => _furniEditor = furniEditor;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        FurniEditorResponder.Read(session, () => new FurniEditorInteractionsResultComposer(_furniEditor.Interactions(session.GetHabbo())));
        return Task.CompletedTask;
    }
}
