using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Habbicons;

public sealed class RoomUseHabbiconComposer(int roomUserId, int habbiconId) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.RoomUseHabbiconComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(roomUserId);
        packet.WriteInteger(habbiconId);
    }
}
