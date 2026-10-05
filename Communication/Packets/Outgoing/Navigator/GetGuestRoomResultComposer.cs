using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public sealed class GetGuestRoomResultComposer(GuestRoomInfoSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GetGuestRoomResultComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(snapshot.IsLoading);
        packet.WriteUInteger(snapshot.Id);
        packet.WriteString(snapshot.Name);
        packet.WriteInteger(snapshot.OwnerId);
        packet.WriteString(snapshot.OwnerName);
        packet.WriteInteger(snapshot.Access);
        packet.WriteInteger(snapshot.UsersNow);
        packet.WriteInteger(snapshot.UsersMax);
        packet.WriteString(snapshot.Description);
        packet.WriteInteger(snapshot.TradeSettings);
        packet.WriteInteger(snapshot.Score);
        packet.WriteInteger(0); //Top rated room rank.
        packet.WriteInteger(snapshot.Category);
        packet.WriteInteger(snapshot.Tags.Length);
        foreach (var tag in snapshot.Tags) packet.WriteString(tag);
        if (snapshot.Group != null && snapshot.Promotion != null)
        {
            packet.WriteInteger(62);
            packet.WriteInteger(snapshot.Group.Id);
            packet.WriteString(snapshot.Group.Name);
            packet.WriteString(snapshot.Group.Badge);
            packet.WriteString(snapshot.Promotion.Name);
            packet.WriteString(snapshot.Promotion.Description);
            packet.WriteInteger(snapshot.Promotion.MinutesLeft);
        }
        else if (snapshot.Group != null)
        {
            packet.WriteInteger(58);
            packet.WriteInteger(snapshot.Group.Id);
            packet.WriteString(snapshot.Group.Name);
            packet.WriteString(snapshot.Group.Badge);
        }
        else if (snapshot.Promotion != null)
        {
            packet.WriteInteger(60);
            packet.WriteString(snapshot.Promotion.Name);
            packet.WriteString(snapshot.Promotion.Description);
            packet.WriteInteger(snapshot.Promotion.MinutesLeft);
        }
        else
            packet.WriteInteger(56);
        packet.WriteBoolean(snapshot.CheckEntry);
        packet.WriteBoolean(false);
        packet.WriteBoolean(false);
        packet.WriteBoolean(false);
        packet.WriteInteger(snapshot.WhoCanMute);
        packet.WriteInteger(snapshot.WhoCanKick);
        packet.WriteInteger(snapshot.WhoCanBan);
        packet.WriteBoolean(snapshot.CanModify);
        packet.WriteInteger(snapshot.ChatMode);
        packet.WriteInteger(snapshot.ChatSize);
        packet.WriteInteger(snapshot.ChatSpeed);
        packet.WriteInteger(snapshot.ExtraFlood);
        packet.WriteInteger(snapshot.ChatDistance);
    }
}
