using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Session;

public class FlatAccessibleComposer : IServerPacket
{
    private readonly string _username;

    public uint MessageId => ServerPacketHeader.FlatAccessibleComposer;

    public FlatAccessibleComposer(string username)
    {
        _username = username;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteString(_username);
}
