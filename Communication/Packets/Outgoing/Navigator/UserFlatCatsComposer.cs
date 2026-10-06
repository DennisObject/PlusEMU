using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class UserFlatCatsComposer(ImmutableArray<NavigatorCategoryRow> categories) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.UserFlatCatsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(categories.Length);

        foreach (var category in categories) {
            packet.WriteInteger(category.Id);
            packet.WriteString(category.PublicName);
            packet.WriteBoolean(category.CanSelect);
            packet.WriteBoolean(false);
            packet.WriteString(string.Empty);
            packet.WriteString(string.Empty);
            packet.WriteBoolean(false);
        }
    }
}
