using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

public class FurniEditorDeleteEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;

    public FurniEditorDeleteEvent(IFurniEditorService furniEditor) => _furniEditor = furniEditor;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        uint id = packet.ReadUInt();
        session.Send(new FurniEditorResultComposer(_furniEditor.Delete(session.GetHabbo(), id)));
        return Task.CompletedTask;
    }
}
