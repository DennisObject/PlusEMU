using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class ModeratorRoomInfoComposer(ModeratorRoomInfoSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ModeratorRoomInfoComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(snapshot.Id);
        packet.WriteInteger(snapshot.UsersNow);
        packet.WriteBoolean(snapshot.OwnerInRoom);
        packet.WriteInteger(snapshot.OwnerId);
        packet.WriteString(snapshot.OwnerName);
        packet.WriteBoolean(true);
        packet.WriteString(snapshot.Name);
        packet.WriteString(snapshot.Description);
        packet.WriteInteger(snapshot.Tags.Length);

        foreach (var tag in snapshot.Tags)
        {
            packet.WriteString(tag);
        }

        packet.WriteBoolean(false);
    }
}
