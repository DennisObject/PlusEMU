using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Navigator.New;

public sealed class NavigatorSearchResultSetComposer(NavigatorSearchSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.NavigatorSearchResultSetComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(snapshot.Category);
        packet.WriteString(snapshot.Query);
        packet.WriteInteger(snapshot.Results.Length);
        foreach (var result in snapshot.Results)
        {
            packet.WriteString(result.CategoryIdentifier);
            packet.WriteString(result.PublicName);
            packet.WriteInteger(result.Action);
            packet.WriteBoolean(false);
            packet.WriteInteger(result.ViewMode);
            packet.WriteInteger(result.Rooms.Length);
            foreach (var room in result.Rooms) RoomAppender.WriteRoom(packet, room);
        }
    }
}
