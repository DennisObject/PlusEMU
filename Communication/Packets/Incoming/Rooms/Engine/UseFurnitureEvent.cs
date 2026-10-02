using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class UseFurnitureEvent : RoomPacketEvent
{
    private readonly IQuestManager _questManager;
    private readonly IDatabase _database;

    public UseFurnitureEvent(IQuestManager questManager, IDatabase database)
    {
        _questManager = questManager;
        _database = database;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null)
            return Task.CompletedTask;
        var before = item.ExtraData.Serialize();
        try
        {
        var hasRights = room.CheckRights(session, false, true);
        if (item.Definition.InteractionType == InteractionType.Banzaitele)
            return Task.CompletedTask;
        if (item.Definition.InteractionType == InteractionType.Toner)
        {
            if (!room.CheckRights(session, true))
                return Task.CompletedTask;
            room.TonerData.Enabled = room.TonerData.Enabled == 0 ? 1 : 0;
            room.SendPacket(new ObjectUpdateComposer(item));
            item.UpdateState();
            using var dbClient = _database.GetQueryReactor();
            dbClient.RunQuery($"UPDATE `room_items_toner` SET `enabled` = '{room.TonerData.Enabled}' LIMIT 1");
            return Task.CompletedTask;
        }
        if (item.Definition.InteractionType == InteractionType.GnomeBox && (item.OwnerId == session.GetHabbo().Id || item.UserId == session.GetHabbo().Id)) session.Send(new GnomeBoxComposer(item.Id));
        var toggle = true;
        if (item.Definition.InteractionType == InteractionType.WfFloorSwitch1 || item.Definition.InteractionType == InteractionType.WfFloorSwitch2)
        {
            var user = item.GetRoom().GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
            if (user == null)
                return Task.CompletedTask;
            if (!Gamemap.TilesTouching(item.GetX, item.GetY, user.X, user.Y)) toggle = false;
        }
        var request = packet.ReadInt();
        item.Interactor.OnTrigger(session, item, request, hasRights);
        if (toggle)
            item.GetRoom().GetWired().TriggerEvent(WiredBoxType.TriggerStateChanges, session.GetHabbo(), item);
        _questManager.ProgressUserQuest(session, QuestType.ExploreFindItem, (int)item.Definition.Id); 
        return Task.CompletedTask;
        }
        finally
        {
            if (item.ExtraData.Serialize() != before)
                RewardTrackManager.Current?.Progress(session, RewardTrackActions.SwitchItemState);
        }
    }
}