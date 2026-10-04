using Plus.HabboHotel.Permissions;
using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Rooms.Engine;

internal class PickupObjectEvent : IPacketEvent
{
    private readonly IGameClientManager _clientManager;
    private readonly IQuestManager _questManager;
    private readonly IDatabase _database;

    public PickupObjectEvent(IGameClientManager clientManager, IQuestManager questManager, IDatabase database)
    {
        _clientManager = clientManager;
        _questManager = questManager;
        _database = database;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return;
        packet.ReadInt(); //unknown
        var itemId = packet.ReadUInt();
        var item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary)
            return;
        if (item.Definition.InteractionType == InteractionType.Postit)
            return;
        var itemRights = false;
        if (item.UserId == session.GetHabbo().Id || room.CheckRights(session, false))
            itemRights = true;
        else if (room.Group != null && room.CheckRights(session, false, true)) //Room has a group, this user has group rights.
            itemRights = true;
        else if (session.GetHabbo().Access.Can(PermissionKeys.RoomItemTake))
            itemRights = true;
        if (itemRights)
        {
            using var connection = _database.Connection();
            if (item.Definition.InteractionType == InteractionType.Tent || item.Definition.InteractionType == InteractionType.TentSmall)
                room.RemoveTent(item.Id);
            if (item.Definition.InteractionType == InteractionType.Moodlight)
            {
                await connection.ExecuteAsync("DELETE FROM `room_items_moodlight` WHERE `item_id` = @id LIMIT 1", new { id = item.Id });
            }
            else if (item.Definition.InteractionType == InteractionType.Toner)
            {
                await connection.ExecuteAsync("DELETE FROM `room_items_toner` WHERE `id` = @id LIMIT 1", new { id = item.Id });
            }
            if (item.UserId == session.GetHabbo().Id || session.GetHabbo().Access.Can(PermissionKeys.RoomItemTake))
            {
                room.GetRoomItemHandler().RemoveFurniture(session, item.Id);
                session.GetHabbo().Inventory.Furniture.AddItem(item.ToInventoryItem());
                session.Send(new FurniListUpdateComposer());
            }
            else //Item is being ejected.
            {
                var targetClient = _clientManager.GetClientByUserId(item.UserId);
                if (targetClient != null && targetClient.GetHabbo() != null) //Again, do we have an active client?
                {
                    room.GetRoomItemHandler().RemoveFurniture(targetClient, item.Id);
                    targetClient.GetHabbo().Inventory.Furniture.AddItem(item.ToInventoryItem());
                    targetClient.Send(new FurniListUpdateComposer());
                }
                else //No, query time.
                {
                    room.GetRoomItemHandler().RemoveFurniture(null, item.Id);
                }
            }

            await connection.ExecuteAsync("UPDATE `items` SET `room_id` = '0' WHERE `id` = @id LIMIT 1", new { id = item.Id });

            _questManager.ProgressUserQuest(session, QuestType.FurniPick);
        }
    }
}
