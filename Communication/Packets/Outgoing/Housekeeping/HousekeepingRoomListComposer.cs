using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public class HousekeepingRoomListComposer : IServerPacket
{
    private readonly IReadOnlyList<HousekeepingRoom> _rooms;
    public uint MessageId => ServerPacketHeader.HousekeepingRoomListComposer;

    public HousekeepingRoomListComposer(IReadOnlyList<HousekeepingRoom> rooms) => _rooms = rooms.ToArray();

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_rooms.Count);
        foreach (var room in _rooms)
            HousekeepingRoomDetailComposer.WriteRoom(packet, room);
    }
}
