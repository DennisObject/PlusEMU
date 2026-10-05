using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public sealed class RoomEventComposer(RoomEventSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.RoomEventComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(data.RoomId);
        packet.WriteInteger(data.OwnerId);
        packet.WriteString(data.OwnerName);
        packet.WriteInteger(data.Active ? 1 : 0);
        packet.WriteInteger(0);
        packet.WriteString(data.Name);
        packet.WriteString(data.Description);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
        packet.WriteInteger(0);
    }
}
