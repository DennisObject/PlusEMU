using Plus.Database;
using Dapper;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class SellRoomCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Description => "Allows the user to sell their own room.";

    public string Parameters => "%price%";

    public string Key => "sellroom";

    public SellRoomCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!room.CheckRights(session, true))
            return;
        if (!parameters.Any())
        {
            session.SendWhisper("Oops, you forgot to choose a price to sell the room for.");
            return;
        }
        if (room.Group != null)
        {
            session.SendWhisper("Oops, this room has a group. You must delete the group before you can sell the room.");
            return;
        }
        if (!int.TryParse(parameters[0], out var price))
        {
            session.SendWhisper("Oops, you've entered an invalid integer.");
            return;
        }
        if (price == 0)
        {
            session.SendWhisper("Oops, you cannot sell a room for 0 credits.");
            return;
        }
        using var connection = _database.Connection();
        connection.Execute("UPDATE rooms SET sale_price=@price WHERE id=@roomId LIMIT 1", new { price, roomId = room.Id });
        session.SendNotification(
            "Your room is now up for sale. The the current room visitors have been alerted, any item that belongs to you in this room will be transferred to the new owner once purchased. Other items shall be ejected.");
        foreach (var user in room.GetRoomUserManager().GetRoomUsers())
        {
            if (user == null || user.GetClient() == null)
                continue;
            user.GetClient().SendWhisper($"Attention! This room has been put up for sale, you can buy it now for {price} credits! Use the :buyroom command.");
        }
    }
}
