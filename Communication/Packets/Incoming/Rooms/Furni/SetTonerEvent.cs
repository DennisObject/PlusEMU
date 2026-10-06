using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class SetTonerEvent : RoomPacketEvent
{
    private readonly IDatabase _database;
    public SetTonerEvent(IDatabase database)
    {
        _database = database;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        if (!room.CheckRights(session, true))
            return Task.CompletedTask;
        if (room.TonerData == null)
            return Task.CompletedTask;
        var item = room.GetRoomItemHandler().GetItem(room.TonerData.ItemId);
        if (item == null || item.IsTemporary || item.Definition.InteractionType != InteractionType.Toner)
            return Task.CompletedTask;
        packet.ReadInt(); //id
        var int1 = packet.ReadInt();
        var int2 = packet.ReadInt();
        var int3 = packet.ReadInt();
        if (int1 > 255 || int2 > 255 || int3 > 255)
            return Task.CompletedTask;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("UPDATE `room_items_toner` SET `enabled` = '1', `data1` = @data1, `data2` = @data2, `data3` = @data3 WHERE `id` = @itemId LIMIT 1");
            dbClient.AddParameter("itemId", item.Id);
            dbClient.AddParameter("data1", int1);
            dbClient.AddParameter("data3", int3);
            dbClient.AddParameter("data2", int2);
            dbClient.RunQuery();
        }
        room.TonerData.Hue = int1;
        room.TonerData.Saturation = int2;
        room.TonerData.Lightness = int3;
        room.TonerData.Enabled = 1;
        room.SendPacket(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));
        item.UpdateState();
        return Task.CompletedTask;
    }
}