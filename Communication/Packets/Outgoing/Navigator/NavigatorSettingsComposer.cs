using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class NavigatorSettingsComposer : IServerPacket
{
    private readonly uint _homeroom;
    public uint MessageId => ServerPacketHeader.NavigatorSettingsComposer;

    public NavigatorSettingsComposer(uint homeroom)
    {
        _homeroom = homeroom;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_homeroom);
        packet.WriteUInteger(_homeroom);
    }
}
