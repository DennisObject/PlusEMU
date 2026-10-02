using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Outgoing.Habbicons;

public sealed class HabbiconShopDataComposer(HabbiconSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HabbiconShopDataComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(snapshot.Collections.Count);
        foreach (var collection in snapshot.Collections)
        {
            packet.WriteInteger(collection.Id);
            packet.WriteString(collection.Name);
            packet.WriteBoolean(collection.Completed);
            packet.WriteInteger(collection.RewardId);
            packet.WriteInteger(collection.RewardState);
            packet.WriteInteger(collection.Credits);
            packet.WriteInteger(collection.Points);
            packet.WriteInteger(collection.PointsType);
            packet.WriteInteger(collection.Items.Count);
            foreach (var item in collection.Items) HabbiconInfoComposer.WriteItem(packet, item);
        }
    }
}
