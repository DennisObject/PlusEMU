using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class FloorHeightMapComposer : IServerPacket
{
    public readonly record struct AreaHide(int FurniId, bool On, int RootX, int RootY, int Width, int Length, bool Invert);

    private readonly bool _zoomIn;
    private readonly string _map;
    private readonly int _wallHeight;
    private readonly IReadOnlyList<AreaHide> _hides;
    private readonly int _cameraX;
    private readonly int _cameraY;
    private readonly float _cameraZ;
    public uint MessageId => ServerPacketHeader.FloorHeightMapComposer;

    public FloorHeightMapComposer(string map, int wallHeight, bool zoomIn = true, IReadOnlyList<AreaHide>? hides = null, int cameraX = 0, int cameraY = 0, float cameraZ = 0)
    {
        _map = map;
        _wallHeight = wallHeight;
        _zoomIn = zoomIn;
        _hides = hides?.ToArray() ?? Array.Empty<AreaHide>();
        _cameraX = cameraX;
        _cameraY = cameraY;
        _cameraZ = cameraZ;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_zoomIn);
        packet.WriteInteger(_wallHeight);
        packet.WriteString(_map);
        packet.WriteInteger(_hides.Count);

        foreach (var hide in _hides) {
            packet.WriteInteger(hide.FurniId);
            packet.WriteBoolean(hide.On);
            packet.WriteInteger(hide.RootX);
            packet.WriteInteger(hide.RootY);
            packet.WriteInteger(hide.Width);
            packet.WriteInteger(hide.Length);
            packet.WriteBoolean(hide.Invert);
        }

        packet.WriteInteger(_cameraX);
        packet.WriteInteger(_cameraY);
        // Big-endian IEEE-754 bits. WriteInteger already writes big-endian, and there is no float writer.
        packet.WriteInteger(BitConverter.SingleToInt32Bits(_cameraZ));
    }
}
