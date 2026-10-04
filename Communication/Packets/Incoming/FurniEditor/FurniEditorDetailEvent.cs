using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

public class FurniEditorDetailEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;

    public FurniEditorDetailEvent(IFurniEditorService furniEditor) => _furniEditor = furniEditor;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        uint id = packet.ReadUInt();
        FurniEditorResponder.Read(session, () => new FurniEditorDetailResultComposer(_furniEditor.Detail(session.GetHabbo(), id)));
        return Task.CompletedTask;
    }
}
