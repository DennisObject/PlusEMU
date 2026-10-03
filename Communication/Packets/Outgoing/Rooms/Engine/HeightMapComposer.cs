using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class HeightMapComposer : IServerPacket
{
    private readonly string _map;
    private readonly short[,]? _placement;
    public uint MessageId => ServerPacketHeader.HeightMapComposer;

    public HeightMapComposer(string map)
    {
        _map = map;
    }

    public HeightMapComposer(short[,] placement)
    {
        _map = "";
        _placement = placement;
    }

    public void Compose(IOutgoingPacket packet)
    {
        if (_placement != null)
        {
            packet.WriteInteger(_placement.GetLength(0));
            packet.WriteInteger(_placement.Length);
            for (var row = 0; row < _placement.GetLength(1); row++)
                for (var column = 0; column < _placement.GetLength(0); column++) packet.WriteShort(_placement[column, row]);
            return;
        }
        var split = _map.Replace("\n", "").Split('\r');
        packet.WriteInteger(split[0].Length);
        packet.WriteInteger((split.Length - 1) * split[0].Length);
        var x = 0;
        var y = 0;
        for (y = 0; y < split.Length - 1; y++)
        {
            for (x = 0; x < split[0].Length; x++)
            {
                char pos;
                try
                {
                    pos = split[y][x];
                }
                catch
                {
                    pos = 'x';
                }
                if (pos == 'x')
                    packet.WriteShort(-1);
                else
                {
                    var height = 0;
                    if (int.TryParse(pos.ToString(), out height))
                        height = height * 256;
                    else
                        height = (Convert.ToInt32(pos) - 87) * 256;
                    packet.WriteShort((short)height);
                }
            }
        }
    }
}