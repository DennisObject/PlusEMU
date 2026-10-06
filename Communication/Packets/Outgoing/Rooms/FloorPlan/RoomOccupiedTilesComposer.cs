using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.FloorPlan;

public class RoomOccupiedTilesComposer : IServerPacket
{
    private readonly IReadOnlyList<(int X, int Y)> _tiles;

    public uint MessageId => ServerPacketHeader.RoomOccupiedTilesComposer;

    public RoomOccupiedTilesComposer(IReadOnlyList<(int X, int Y)> tiles)
    {
        _tiles = tiles.ToArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_tiles.Count);

        foreach (var (x, y) in _tiles)
        {
            packet.WriteInteger(x);
            packet.WriteInteger(y);
        }
    }
}
