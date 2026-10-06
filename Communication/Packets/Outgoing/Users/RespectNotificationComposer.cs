using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Users;

public class RespectNotificationComposer : IServerPacket
{
    private readonly int _userId;
    private readonly int _respect;
    public uint MessageId => ServerPacketHeader.RespectNotificationComposer;

    public RespectNotificationComposer(int userId, int respect)
    {
        _userId = userId;
        _respect = respect;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_userId);
        packet.WriteInteger(_respect);
    }
}
