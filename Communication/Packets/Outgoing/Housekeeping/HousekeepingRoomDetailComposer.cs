using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public class HousekeepingRoomDetailComposer : IServerPacket
{
    private readonly HousekeepingRoom? _room;
    public uint MessageId => ServerPacketHeader.HousekeepingRoomDetailComposer;

    public HousekeepingRoomDetailComposer(HousekeepingRoom? room) => _room = room;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(_room != null);
        if (_room != null) WriteRoom(packet, _room);
    }

    /// <summary>Shared with the room list; field order matches the renderer's HousekeepingRoomData.</summary>
    internal static void WriteRoom(IOutgoingPacket packet, HousekeepingRoom room)
    {
        packet.WriteInteger(room.Id);
        packet.WriteString(room.Name);
        packet.WriteString(room.Description);
        packet.WriteInteger(room.OwnerId);
        packet.WriteString(room.OwnerName);
        packet.WriteInteger(room.UserCount);
        packet.WriteInteger(room.MaxUsers);
        packet.WriteBoolean(room.IsLocked);
        packet.WriteBoolean(room.IsMuted);
        packet.WriteBoolean(room.IsPublic);
        packet.WriteInteger(room.CreatedAt);
    }
}
