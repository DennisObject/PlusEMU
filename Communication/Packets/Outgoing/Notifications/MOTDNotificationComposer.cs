using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Notifications;

public class MOTDNotificationComposer : IServerPacket
{
    private readonly string _message;

    public uint MessageId => ServerPacketHeader.MOTDNotificationComposer;

    public MOTDNotificationComposer(string message)
    {
        _message = message;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteString(_message);
    }
}
