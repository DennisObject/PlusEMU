using Dapper;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items;

public readonly record struct FurnitureUseRequest(uint ItemId, int Parameter);
public readonly record struct FurnitureClickRequest(uint ItemId, bool IsWall);

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
    void Click(Room room, GameClient session, FurnitureClickRequest request);
    void TurnOffDice(Room room, GameClient session, uint itemId);
    void RollDice(Room room, GameClient session, FurnitureUseRequest request);
    void UseOneWayGate(Room room, GameClient session, uint itemId);
    void UseWall(Room room, GameClient session, FurnitureUseRequest request);
}

public sealed class FurnitureUseService(IFurnitureUseStore store, IQuestManager quests) : IFurnitureUseService
{
    public void TurnOffDice(Room room, GameClient session, uint itemId)
    {
        var item = FindPermanent(room, session, itemId);
        if (item == null) return;
        item.Interactor.OnTrigger(session, item, -1, room.CheckRights(session));
    }

    public void RollDice(Room room, GameClient session, FurnitureUseRequest request)
    {
        var item = FindPermanent(room, session, request.ItemId);
        if (item == null) return;
        item.Interactor.OnTrigger(session, item, request.Parameter, room.CheckRights(session, false, true));
    }

    public void UseOneWayGate(Room room, GameClient session, uint itemId)
    {
        var item = FindPermanent(room, session, itemId);
        if (item?.Definition.InteractionType != InteractionType.OneWayGate) return;
        item.Interactor.OnTrigger(session, item, -1, room.CheckRights(session));
    }

    public void UseWall(Room room, GameClient session, FurnitureUseRequest request)
    {
        var item = FindPermanent(room, session, request.ItemId);
        if (item == null) return;
        item.Interactor.OnTrigger(session, item, request.Parameter, room.CheckRights(session, false, true));
        room.GetWired().TriggerEvent(WiredBoxType.TriggerStateChanges, session.GetHabbo(), item);
        quests.ProgressUserQuest(session, QuestType.ExploreFindItem, (int)item.Definition.Id);
    }

    private static Item? FindPermanent(Room room, GameClient session, uint itemId)
    {
        if (!ReferenceEquals(session.GetHabbo().CurrentRoom, room)) return null;
        var item = room.GetRoomItemHandler().GetItem(itemId);
        return item is { IsTemporary: false } ? item : null;
    }

    public void Click(Room room, GameClient session, FurnitureClickRequest request)
    {
        var actor = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (actor == null || actor.IsBot) return;
        var item = room.GetRoomItemHandler().GetItem(request.ItemId);
        if (item == null || request.IsWall && !item.IsWallItem || !request.IsWall && !item.IsFloorItem) return;
        if (item.IsTemporary && !room.GetRoomItemHandler().OwnsTemporary(item)) return;
        room.GetWired().Dispatch(new(WiredEventKind.ClickFurni) { Actor = actor, EventItem = item });
        if (!request.IsWall && string.Equals(item.Definition.InteractionName, "room_invisible_click_tile", StringComparison.OrdinalIgnoreCase))
            room.GetWired().Dispatch(new(WiredEventKind.ClickTile) { Actor = actor, EventItem = item, X = item.GetX, Y = item.GetY });
    }

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
