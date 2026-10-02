using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Outgoing.Habbicons;

public sealed class HabbiconInfoComposer(HabbiconItem item) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HabbiconInfoComposer;
    public void Compose(IOutgoingPacket packet) => WriteItem(packet, item);
    internal static void WriteItem(IOutgoingPacket packet, HabbiconItem item)
    {
        packet.WriteInteger(item.Id);
        packet.WriteString(item.Name);
        packet.WriteInteger(item.CollectionId);
        packet.WriteInteger(item.State);
        packet.WriteInteger(item.Credits);
        packet.WriteInteger(item.Points);
        packet.WriteInteger(item.PointsType);
    }
}
