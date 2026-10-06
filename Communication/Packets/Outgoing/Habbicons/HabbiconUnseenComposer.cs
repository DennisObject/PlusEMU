using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Outgoing.Habbicons;

public sealed class HabbiconUnseenComposer(IReadOnlyList<int> ids) : IServerPacket
{
    private readonly IReadOnlyList<int> _captured = ids.ToArray();
    public uint MessageId => ServerPacketHeader.FurniListNotificationComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(HabbiconService.UnseenCategory);
        packet.WriteInteger(_captured.Count);

        foreach (var id in _captured) {
            packet.WriteInteger(id);
        }
    }
}
