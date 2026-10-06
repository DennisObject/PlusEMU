using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator.New;

// TODO @80O: Implement
public class NavigatorCollapsedCategoriesComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.NavigatorCollapsedCategoriesComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(0);
}
