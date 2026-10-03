using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class UserRightsComposer : IServerPacket
{
    private readonly int _rank;
    private readonly bool _isAmbassador;
    private readonly bool _hasCamera;
    public uint MessageId => ServerPacketHeader.UserRightsComposer;

    public UserRightsComposer(int rank, bool isAmbassador, bool hasCamera = false)
    {
        _rank = rank;
        _isAmbassador = isAmbassador;
        _hasCamera = hasCamera;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(2); //Club level
        packet.WriteInteger(_rank);
        packet.WriteBoolean(_isAmbassador); //Is an ambassador
        // Octane's optional rank metadata and resolved permission block.
        packet.WriteInteger(_rank);
        packet.WriteString(""); // Rank name
        packet.WriteString(""); // Rank badge
        packet.WriteInteger(_hasCamera ? 1 : 0);
        if (_hasCamera)
        {
            packet.WriteString("acc_camera");
            packet.WriteInteger(1);
        }
    }
}