using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Rooms.Settings;

public sealed class GetRoomBannedUsersComposer(RoomBannedUsersSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GetRoomBannedUsersComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(data.RoomId);
        packet.WriteInteger(data.Users.Length);
        foreach (var user in data.Users) { packet.WriteInteger(user.Id); packet.WriteString(user.Username); }
    }
}
