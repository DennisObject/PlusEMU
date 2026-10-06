using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class HeightMapComposer : IServerPacket
{
    private readonly int _width;
    private readonly ImmutableArray<short> _heights;
    public uint MessageId => ServerPacketHeader.HeightMapComposer;

    public HeightMapComposer(short[,] placement)
    {
        _width = placement.GetLength(0);
        var heights = ImmutableArray.CreateBuilder<short>(placement.Length);

        for (var row = 0; row < placement.GetLength(1); row++) {
            for (var column = 0; column < _width; column++) {
                heights.Add(placement[column, row]);
            }
        }

        _heights = heights.MoveToImmutable();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_width);
        packet.WriteInteger(_heights.Length);

        foreach (var height in _heights) {
            packet.WriteShort(height);
        }
    }
}
