using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class UseWallItemEvent : RoomPacketEvent
{
    private readonly IQuestManager _questManager;

    public UseWallItemEvent(IQuestManager questManager)
    {
        _questManager = questManager;
    }

    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadUInt();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null)
            return Task.CompletedTask;
        var hasRights = room.CheckRights(session, false, true);
        var request = packet.ReadInt();
        item.Interactor.OnTrigger(session, item, request, hasRights);
        item.GetRoom().GetWired().TriggerEvent(WiredBoxType.TriggerStateChanges, session.GetHabbo(), item);
        _questManager.ProgressUserQuest(session, QuestType.ExploreFindItem, (int)item.Definition.Id);
        return Task.CompletedTask;
    }
}