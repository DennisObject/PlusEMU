using Dapper;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items;

public readonly record struct FurnitureUseRequest(uint ItemId, int Parameter);

public interface IFurnitureUseStore
{
    void SetTonerEnabled(uint itemId, uint roomId, bool enabled);
}

public sealed class FurnitureUseStore(IDatabase database) : IFurnitureUseStore
{
    public void SetTonerEnabled(uint itemId, uint roomId, bool enabled)
    {
        using var connection = database.Connection();
        if (connection.Execute("""
            UPDATE room_items_toner toner JOIN items item ON item.id=toner.id
            SET toner.enabled=@enabled WHERE toner.id=@itemId AND item.room_id=@roomId
            """, new { itemId, roomId, enabled }) != 1)
            throw new InvalidOperationException("Toner state was not persisted.");
    }
}

public interface IFurnitureUseService
{
    void Use(Room room, GameClient session, FurnitureUseRequest request);
}

public sealed class FurnitureUseService(IFurnitureUseStore store, IQuestManager quests) : IFurnitureUseService
{
    public void Use(Room room, GameClient session, FurnitureUseRequest request)
    {
        var habbo = session.GetHabbo();
        if (habbo.CurrentRoom != room) return;
        var item = room.GetRoomItemHandler().GetItem(request.ItemId);
        if (item == null || item.IsTemporary || item.RoomId != room.Id || item.Definition == null) return;
        if (item.Definition.InteractionType == InteractionType.Banzaitele) return;
        if (item.Definition.InteractionType == InteractionType.Toner)
        {
            lock (room.NavigationSync)
            {
                if (habbo.CurrentRoom != room || !room.CheckRights(session, true) ||
                    room.GetRoomItemHandler().GetItem(item.Id) != item || room.TonerData?.ItemId != item.Id) return;
                var enabled = room.TonerData.Enabled == 0;
                store.SetTonerEnabled(item.Id, room.Id, enabled);
                room.TonerData.Enabled = enabled ? 1 : 0;
                room.SendPacket(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));
                item.UpdateState();
            }
            return;
        }
        var hasRights = room.CheckRights(session, false, true);
        if (item.Definition.InteractionType == InteractionType.GnomeBox && item.OwnerId == habbo.Id)
            session.Send(new GnomeBoxComposer(item.Id));
        var toggle = true;
        if (item.Definition.InteractionType is InteractionType.WfFloorSwitch1 or InteractionType.WfFloorSwitch2)
        {
            var user = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
            if (user == null) return;
            toggle = Gamemap.TilesTouching(item.GetX, item.GetY, user.X, user.Y);
        }
        item.Interactor.OnTrigger(session, item, request.Parameter, hasRights);
        if (toggle) room.GetWired().TriggerEvent(WiredBoxType.TriggerStateChanges, habbo, item);
        quests.ProgressUserQuest(session, QuestType.ExploreFindItem, (int)item.Definition.Id);
    }
}
