using Plus.Communication.Packets.Outgoing.FurniEditor;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Incoming.FurniEditor;

public class FurniEditorBySpriteEvent : IPacketEvent
{
    private readonly IFurniEditorService _furniEditor;

    public FurniEditorBySpriteEvent(IFurniEditorService furniEditor) => _furniEditor = furniEditor;

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        int spriteId = packet.ReadInt();
        FurniEditorResponder.Read(session, () => new FurniEditorDetailResultComposer(_furniEditor.DetailBySprite(session.GetHabbo(), spriteId)));
        return Task.CompletedTask;
    }
}
