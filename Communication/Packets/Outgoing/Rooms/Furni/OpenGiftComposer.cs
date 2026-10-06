using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni;

public sealed record OpenGiftWireData(string Type, int SpriteId, string ItemName, uint ItemId, bool IsInRoom, string ExtraData);

public sealed class OpenGiftComposer(OpenGiftWireData data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.OpenGiftComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(data.Type);
        packet.WriteInteger(data.SpriteId);
        packet.WriteString(data.ItemName);
        packet.WriteUInteger(data.ItemId);
        packet.WriteString(data.Type);
        packet.WriteBoolean(data.IsInRoom);
        packet.WriteString(data.ExtraData);
    }
}
