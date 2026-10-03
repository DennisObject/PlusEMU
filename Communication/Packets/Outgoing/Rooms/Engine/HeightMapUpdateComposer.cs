using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class HeightMapUpdateComposer(IReadOnlyList<HeightMapUpdateComposer.Tile> tiles) : IServerPacket
{
    public readonly record struct Tile(int X, int Y, short Value);
    public uint MessageId => ServerPacketHeader.HeightMapUpdateComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteByte((byte)tiles.Count);
        foreach (var tile in tiles)
        {
            packet.WriteByte((byte)tile.X);
            packet.WriteByte((byte)tile.Y);
            packet.WriteShort(tile.Value);
        }
    }
}
