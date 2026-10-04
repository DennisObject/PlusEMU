using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

public class FurniEditorUpdateEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;

    public FurniEditorUpdateEvent(IFurniEditorService furniEditor) => _furniEditor = furniEditor;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        uint id = packet.ReadUInt();
        string json = packet.ReadString();
        session.Send(new FurniEditorResultComposer(_furniEditor.Update(session.GetHabbo(), id, json)));
        return Task.CompletedTask;
    }
}
