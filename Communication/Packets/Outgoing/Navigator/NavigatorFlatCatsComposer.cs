using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class NavigatorFlatCatsComposer(ImmutableArray<NavigatorCategoryRow> categories) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.NavigatorFlatCatsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(categories.Length);

        foreach (var category in categories)
        {
            packet.WriteInteger(category.Id);
            packet.WriteString(category.PublicName);
            packet.WriteBoolean(category.CanSelect);
        }
    }
}
