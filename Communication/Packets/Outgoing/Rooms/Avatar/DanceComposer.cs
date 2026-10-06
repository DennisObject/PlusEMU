using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Avatar;

public class DanceComposer : IServerPacket
{
    private readonly int _virtualId;
    private readonly int _dance;

    public uint MessageId => ServerPacketHeader.DanceComposer;

    public DanceComposer(int virtualId, int dance)
    {
        _virtualId = virtualId;
        _dance = dance;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_virtualId);
        packet.WriteInteger(_dance);
    }
}
