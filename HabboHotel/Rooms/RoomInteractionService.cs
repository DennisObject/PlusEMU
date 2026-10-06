using Dapper;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms;

public interface IRoomInteractionStore
{
    int AddRating(uint roomId, int rating);
    void DeleteSticky(uint itemId, uint roomId);
}

public sealed class RoomInteractionStore(IDatabase database) : IRoomInteractionStore
{
    public int AddRating(uint roomId, int rating)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.Execute("UPDATE rooms SET score=score+@rating WHERE id=@roomId LIMIT 1", new { roomId, rating }, transaction) != 1)
            throw new InvalidOperationException("Room rating was not persisted.");
        var score = connection.QuerySingle<int>("SELECT score FROM rooms WHERE id=@roomId", new { roomId }, transaction);
        transaction.Commit();
        return score;
    }

    public void DeleteSticky(uint itemId, uint roomId)
    {
        using var connection = database.Connection();
        if (connection.Execute("DELETE FROM items WHERE id=@itemId AND room_id=@roomId LIMIT 1", new { itemId, roomId }) != 1)
            throw new InvalidOperationException("Sticky note was not deleted.");
    }
}

public interface IRoomInteractionService
{
    void Rate(Room room, GameClient session, int rating);
    void DeleteSticky(Room room, GameClient session, uint itemId);
}

public sealed class RoomInteractionService(IRoomInteractionStore store) : IRoomInteractionService
{
    public void Rate(Room room, GameClient session, int rating)
    {
        if (rating is not (-1 or 1)) return;
        lock (room.NavigationSync)
        {
            var habbo = session.GetHabbo();
            if (habbo.CurrentRoom != room || habbo.RatedRooms.Contains(room.RoomId) || room.CheckRights(session, true)) return;
            var score = store.AddRating(room.RoomId, rating);
            room.Score = score;
            habbo.RatedRooms.Add(room.RoomId);
            session.Send(new RoomRatingComposer(score, false));
        }
    }

    public void DeleteSticky(Room room, GameClient session, uint itemId)
    {
        lock (room.NavigationSync)
        {
            if (session.GetHabbo().CurrentRoom != room || !room.CheckRights(session)) return;
            var item = room.GetRoomItemHandler().GetItem(itemId);
            if (item == null || item.IsTemporary || item.RoomId != room.RoomId ||
                item.Definition?.InteractionType is not (InteractionType.Postit or InteractionType.CameraPicture)) return;
            store.DeleteSticky(item.Id, room.RoomId);
            room.GetRoomItemHandler().RemoveFurniture(session, item.Id);
        }
    }
}
