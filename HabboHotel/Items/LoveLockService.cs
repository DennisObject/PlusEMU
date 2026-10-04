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

public sealed class LoveLockService(ILoveLockStore store) : ILoveLockService
{
    public void Confirm(GameClient session, LoveLockConfirmation confirmation)
    {
        var room = session.GetHabbo().CurrentRoom;
        if (room == null) return;
        var item = room.GetRoomItemHandler().GetItem(confirmation.ItemId);
        if (item == null || item.IsTemporary || item.Definition?.InteractionType != InteractionType.Lovelock) return;
        var one = room.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser);
        var two = room.GetRoomUserManager().GetRoomUserByHabbo(item.InteractingUser2);
        if (one?.GetClient() == null || two?.GetClient() == null) { Cancel(item, one, two, session, true); return; }
        var actorId = session.GetHabbo().Id;
        if ((actorId != item.InteractingUser && actorId != item.InteractingUser2) ||
            (item.UserId != item.InteractingUser && item.UserId != item.InteractingUser2)) return;
        if (item.ExtraData.Serialize().Contains((char)5)) { Cancel(item, one, two, session, false); return; }
        if (!confirmation.Confirmed) { Cancel(item, one, two, session, false, false); return; }

        var actor = actorId;
        var completes = actor == item.InteractingUser ? two!.LlPartner != 0 : one!.LlPartner != 0;
        if (!completes)
        {
            if (actor == item.InteractingUser) one.LlPartner = item.InteractingUser2;
            else two.LlPartner = item.InteractingUser;
            session.Send(new LoveLockDialogueSetLockedComposer(confirmation.ItemId));
            return;
        }

        var oneClient = one!.GetClient()!; var twoClient = two!.GetClient()!;
        var data = $"1{(char)5}{one.GetUsername()}{(char)5}{two.GetUsername()}{(char)5}{oneClient.GetHabbo().Look}{(char)5}{twoClient.GetHabbo().Look}{(char)5}{DateTime.Now:dd/MM/yyyy}";
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

    private static void Cancel(Item item, RoomUser? one, RoomUser? two, GameClient session, bool partnerLeft, bool notify = true)
    {
        item.InteractingUser = item.InteractingUser2 = 0;
        var message = partnerLeft ? "Your partner has left the room or has cancelled the love lock." : "It appears this love lock has already been locked.";
        var notified = false;
        foreach (var user in new[] { one, two }.Where(user => user != null))
        {
            user!.LlPartner = 0; user.CanWalk = true;
            if (notify && user.GetClient() != null) { user.GetClient().SendNotification(message); notified = true; }
        }
        if (notify && !notified) session.SendNotification(message);
    }
}
