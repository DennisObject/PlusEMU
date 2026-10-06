using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class UserRemoveComposer : IServerPacket
{
    private readonly int _id;

    public uint MessageId => ServerPacketHeader.UserRemoveComposer;

    public UserRemoveComposer(int id)
    {
        _id = id;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteString(_id.ToString());
}
