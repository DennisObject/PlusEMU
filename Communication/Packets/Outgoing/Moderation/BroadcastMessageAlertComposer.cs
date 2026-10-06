using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class BroadcastMessageAlertComposer : IServerPacket
{
    private readonly string _message;
    private readonly string _url;
    public uint MessageId => ServerPacketHeader.BroadcastMessageAlertComposer;

    public BroadcastMessageAlertComposer(string message, string url = "")
    {
        _message = message;
        _url = url;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_message);
        packet.WriteString(_url);
    }
}
