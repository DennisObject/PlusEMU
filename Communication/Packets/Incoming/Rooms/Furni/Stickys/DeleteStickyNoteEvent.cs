using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;

internal class DeleteStickyNoteEvent : RoomPacketEvent
{
    private readonly IDatabase _database;

    public DeleteStickyNoteEvent(IDatabase database)
    {
        _database = database;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session))
            return Task.CompletedTask;
        var item = room.GetRoomItemHandler().GetItem(packet.ReadUInt());
        if (item == null || item.IsTemporary)
            return Task.CompletedTask;
        if (item.Definition.InteractionType == InteractionType.Postit || item.Definition.InteractionType == InteractionType.CameraPicture)
        {
            room.GetRoomItemHandler().RemoveFurniture(session, item.Id);
            using var dbClient = _database.GetQueryReactor();
            dbClient.RunQuery($"DELETE FROM `items` WHERE `id` = '{item.Id}' LIMIT 1");
        }
        return Task.CompletedTask;
    }
}