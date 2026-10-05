using Dapper;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.LoveLocks;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items;

public sealed record LoveLockConfirmation(uint ItemId, bool Confirmed);

public interface ILoveLockStore { void Lock(uint itemId, uint roomId, string data); }

public sealed class LoveLockStore(IDatabase database) : ILoveLockStore
{
    public void Lock(uint itemId, uint roomId, string data)
    {
        using var connection = database.Connection();
        if (connection.Execute("UPDATE items SET extra_data=@data WHERE id=@itemId AND room_id=@roomId LIMIT 1", new { itemId, roomId, data }) != 1)
            throw new InvalidOperationException("Love lock was not persisted.");
    }
}

public interface ILoveLockService { void Confirm(GameClient session, LoveLockConfirmation confirmation); }

public sealed class LoveLockService(ILoveLockStore store, TimeProvider timeProvider) : ILoveLockService
{
    public void Confirm(GameClient session, LoveLockConfirmation confirmation)
    {
        var room = session.GetHabbo().CurrentRoom;
        if (room == null) return;
        var item = room.GetRoomItemHandler().GetItem(confirmation.ItemId);
        if (item == null || item.IsTemporary || item.Definition?.InteractionType != InteractionType.Lovelock) return;
        var actorId = session.GetHabbo().Id;
        if (item.RoomId != room.RoomId || (actorId != item.InteractingUser && actorId != item.InteractingUser2) ||
            (item.OwnerId != item.InteractingUser && item.OwnerId != item.InteractingUser2)) return;
        var one = room.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser);
        var two = room.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser2);
        var oneClient = one?.GetClient(); var twoClient = two?.GetClient();
        var oneHabbo = oneClient?.GetHabbo(); var twoHabbo = twoClient?.GetHabbo();
        if (oneHabbo?.CurrentRoom != room || twoHabbo?.CurrentRoom != room)
        { Cancel(item, one, two, oneClient, twoClient, session, true); return; }
        if (item.ExtraData.Serialize().Contains((char)5)) { Cancel(item, one, two, oneClient, twoClient, session, false); return; }
        if (!confirmation.Confirmed) { Cancel(item, one, two, oneClient, twoClient, session, false, false); return; }

        var actor = actorId;
        var completes = actor == item.InteractingUser ? two!.LlPartner != 0 : one!.LlPartner != 0;
        if (!completes)
        {
            if (actor == item.InteractingUser) one.LlPartner = item.InteractingUser2;
            else two.LlPartner = item.InteractingUser;
            session.Send(new LoveLockDialogueSetLockedComposer(confirmation.ItemId));
            return;
        }

        var data = $"1{(char)5}{one!.GetUsername()}{(char)5}{two!.GetUsername()}{(char)5}{oneHabbo.Look}{(char)5}{twoHabbo.Look}{(char)5}{timeProvider.GetUtcNow():dd/MM/yyyy}";
        store.Lock(item.Id, room.RoomId, data);
        item.ExtraData.Store(data);
        item.InteractingUser = item.InteractingUser2 = 0;
        one.LlPartner = two.LlPartner = 0;
        item.UpdateState(true, true);
        oneClient.Send(new LoveLockDialogueCloseComposer(confirmation.ItemId));
        twoClient.Send(new LoveLockDialogueCloseComposer(confirmation.ItemId));
        RewardTrackManager.Current?.Progress(oneClient, RewardTrackActions.FriendFurniLocked);
        RewardTrackManager.Current?.Progress(twoClient, RewardTrackActions.FriendFurniLocked);
        one.CanWalk = two.CanWalk = true;
    }

    private static void Cancel(Item item, RoomUser? one, RoomUser? two, GameClient? oneClient, GameClient? twoClient, GameClient session, bool partnerLeft, bool notify = true)
    {
        item.InteractingUser = item.InteractingUser2 = 0;
        var message = partnerLeft ? "Your partner has left the room or has cancelled the love lock." : "It appears this love lock has already been locked.";
        var notified = false;
        foreach (var pair in new[] { (User: one, Client: oneClient), (User: two, Client: twoClient) }.Where(pair => pair.User != null))
        {
            pair.User!.LlPartner = 0; pair.User.CanWalk = true;
            if (notify && pair.Client != null) { pair.Client.SendNotification(message); notified = true; }
        }
        if (notify && !notified) session.SendNotification(message);
    }
}
