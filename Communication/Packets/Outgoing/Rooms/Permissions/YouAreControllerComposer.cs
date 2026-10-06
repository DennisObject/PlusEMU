using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Permissions;

public class YouAreControllerComposer : IServerPacket
{
    private readonly int _setting;
    public uint MessageId => ServerPacketHeader.YouAreControllerComposer;

    public YouAreControllerComposer(int setting)
    {
        _setting = setting;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(_setting);
}
