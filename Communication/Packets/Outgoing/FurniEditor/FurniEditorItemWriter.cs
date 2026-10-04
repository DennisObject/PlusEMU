using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Editor;

namespace Plus.Communication.Packets.Outgoing.FurniEditor;

// Octane-Renderer's FurniItemData.
internal static class FurniEditorItemWriter
{
    public static void Write(IOutgoingPacket packet, FurniEditorItem item)
    {
        packet.WriteInteger((int)item.Id);
        packet.WriteInteger(item.SpriteId);
        packet.WriteString(item.ItemName);
        packet.WriteString(item.PublicName);
        packet.WriteString(item.Type);
        packet.WriteInteger(item.Width);
        packet.WriteInteger(item.Length);
        packet.WriteDouble(item.StackHeight);
        packet.WriteBoolean(item.AllowStack);
        packet.WriteBoolean(item.AllowWalk);
        packet.WriteBoolean(item.AllowSit);
        packet.WriteBoolean(item.AllowLay);
        packet.WriteString(item.InteractionType);
        packet.WriteInteger(item.InteractionModesCount);
    }
}
