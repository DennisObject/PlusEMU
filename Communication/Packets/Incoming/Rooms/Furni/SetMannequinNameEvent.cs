using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class SetMannequinNameEvent : IPacketEvent
{
    private readonly IDatabase _database;

    public SetMannequinNameEvent(IDatabase database)
    {
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var room = session.GetHabbo().CurrentRoom;
        if (room == null || !room.CheckRights(session, true))
            return Task.CompletedTask;
        var itemId = packet.ReadUInt();
        var name = packet.ReadString();
        var item = session.GetHabbo().CurrentRoom.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary || item.Definition.InteractionType != InteractionType.Mannequin)
            return Task.CompletedTask;
        if (item.LegacyDataString.Contains(Convert.ToChar(5)))
        {
            var flags = item.LegacyDataString.Split(Convert.ToChar(5));
            item.LegacyDataString = flags[0] + Convert.ToChar(5) + flags[1] + Convert.ToChar(5) + name;
        }
        else
            item.LegacyDataString = $"m{Convert.ToChar(5)}.ch-210-1321.lg-285-92{Convert.ToChar(5)}Default Mannequin";
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("UPDATE `items` SET `extra_data` = @Ed WHERE `id` = @itemId LIMIT 1");
            dbClient.AddParameter("itemId", item.Id);
            dbClient.AddParameter("Ed", item.LegacyDataString);
            dbClient.RunQuery();
        }
        item.UpdateState(true, true);
        return Task.CompletedTask;
    }
}