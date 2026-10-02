using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class MoveWallItemEvent : RoomPacketEvent
{
    private readonly IRoomManager _roomManager;

    public MoveWallItemEvent(IRoomManager roomManager)
    {
        _roomManager = roomManager;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session))
            return Task.CompletedTask;
        var itemId = packet.ReadUInt();
        var wallPositionData = packet.ReadString();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary)
            return Task.CompletedTask;
        try
        {
            var wallPos = room.GetRoomItemHandler().WallPositionCheck($":{wallPositionData.Split(':')[1]}");
            item.WallCoordinates = wallPos;
        }
        catch
        {
            return Task.CompletedTask;
        }
        room.GetRoomItemHandler().UpdateItem(item);
        room.SendPacket(new ItemUpdateComposer(item));
        return Task.CompletedTask;
    }
}