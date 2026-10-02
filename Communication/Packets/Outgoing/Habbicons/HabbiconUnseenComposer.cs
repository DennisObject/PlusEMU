using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Outgoing.Habbicons;

public sealed class HabbiconUnseenComposer(IReadOnlyList<int> ids) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.FurniListNotificationComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(HabbiconService.UnseenCategory);
        packet.WriteInteger(ids.Count);
        foreach (var id in ids) packet.WriteInteger(id);
    }
}
