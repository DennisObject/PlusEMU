using System.Data;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.Database;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal class OpenGiftEvent : IPacketEvent
{
    private readonly IItemDataManager _itemDataManger;
    private readonly ICacheManager _cacheManager;
    private readonly IDatabase _database;

    public OpenGiftEvent(IItemDataManager itemDataManager, ICacheManager cacheManager , IDatabase database)
    {
        _itemDataManger = itemDataManager;
        _cacheManager = cacheManager;
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        var presentId = packet.ReadUInt();
        var present = room.GetRoomItemHandler().GetItem(presentId);
        if (present == null || present.IsTemporary)
            return Task.CompletedTask;
        if (present.UserId != session.GetHabbo().Id)
            return Task.CompletedTask;
        DataRow data;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT `base_id`,`extra_data` FROM `user_presents` WHERE `item_id` = @presentId LIMIT 1");
            dbClient.AddParameter("presentId", present.Id);
            data = dbClient.GetRow();
        }
        if (data == null)
        {
            session.SendNotification("Oops! Appears there was a bug with this gift.\nWe'll just get rid of it for you.");
            room.GetRoomItemHandler().RemoveFurniture(null, present.Id);
            using (var dbClient = _database.GetQueryReactor())
            {
                dbClient.RunQuery($"DELETE FROM `items` WHERE `id` = '{present.Id}' LIMIT 1");
                dbClient.RunQuery($"DELETE FROM `user_presents` WHERE `item_id` = '{present.Id}' LIMIT 1");
            }
            session.GetHabbo().Inventory.Furniture.RemoveItem(present.Id);
            session.Send(new FurniListRemoveComposer(present.Id));
            return Task.CompletedTask;
        }
        if (!int.TryParse(present.LegacyDataString.Split(Convert.ToChar(5))[2], out var purchaserId))
        {
            session.SendNotification("Oops! Appears there was a bug with this gift.\nWe'll just get rid of it for you.");
            room.GetRoomItemHandler().RemoveFurniture(null, present.Id);
            using (var dbClient = _database.GetQueryReactor())
            {
                dbClient.RunQuery($"DELETE FROM `items` WHERE `id` = '{present.Id}' LIMIT 1");
                dbClient.RunQuery($"DELETE FROM `user_presents` WHERE `item_id` = '{present.Id}' LIMIT 1");
            }
            session.GetHabbo().Inventory.Furniture.RemoveItem(present.Id);
            session.Send(new FurniListRemoveComposer(present.Id));
            return Task.CompletedTask;
        }
        var purchaser = _cacheManager.GenerateUser(purchaserId);
        if (purchaser == null)
        {
            session.SendNotification("Oops! Appears there was a bug with this gift.\nWe'll just get rid of it for you.");
            room.GetRoomItemHandler().RemoveFurniture(null, present.Id);
            using (var dbClient = _database.GetQueryReactor())
            {
                dbClient.RunQuery($"DELETE FROM `items` WHERE `id` = '{present.Id}' LIMIT 1");
                dbClient.RunQuery($"DELETE FROM `user_presents` WHERE `item_id` = '{present.Id}' LIMIT 1");
            }
            session.GetHabbo().Inventory.Furniture.RemoveItem(present.Id);
            session.Send(new FurniListRemoveComposer(present.Id));
            return Task.CompletedTask;
        }
        if (!_itemDataManger.Items.TryGetValue(Convert.ToUInt32(data["base_id"]), out var baseItem))
        {
            session.SendNotification("Oops, it appears that the item within the gift is no longer in the hotel!");
            room.GetRoomItemHandler().RemoveFurniture(null, present.Id);
            using (var dbClient = _database.GetQueryReactor())
            {
                dbClient.RunQuery($"DELETE FROM `items` WHERE `id` = '{present.Id}' LIMIT 1");
                dbClient.RunQuery($"DELETE FROM `user_presents` WHERE `item_id` = '{present.Id}' LIMIT 1");
            }
            session.GetHabbo().Inventory.Furniture.RemoveItem(present.Id);
            session.Send(new FurniListRemoveComposer(present.Id));
            return Task.CompletedTask;
        }
        present.MagicRemove = true;
        room.SendPacket(new ObjectUpdateComposer(present));
        var thread = new Thread(() => FinishOpenGift(session, baseItem, present, room, data));
        thread.Start();
        return Task.CompletedTask;
    }

    private void FinishOpenGift(GameClient session, ItemDefinition baseItem, Item present, Room room, DataRow row)
    {
        try
        {
            if (baseItem == null || present == null || room == null || row == null)
            Thread.Sleep(1500);
            var itemIsInRoom = true;
            room.GetRoomItemHandler().RemoveFurniture(session, present.Id);
            using (var dbClient = _database.GetQueryReactor())
            {
                dbClient.SetQuery("UPDATE `items` SET `base_item` = @BaseItem, `extra_data` = @edata WHERE `id` = @itemId LIMIT 1");
                dbClient.AddParameter("itemId", present.Id);
                dbClient.AddParameter("BaseItem", row["base_id"]);
                dbClient.AddParameter("edata", row["extra_data"]);
                dbClient.RunQuery();
                dbClient.RunQuery($"DELETE FROM `user_presents` WHERE `item_id` = {present.Id} LIMIT 1");
            }
            present.BaseItem = Convert.ToInt32(row["base_id"]);
            // present.ResetBaseItem(); // TODO @80O: Disabled in item refactor
            present.LegacyDataString = !string.IsNullOrEmpty(Convert.ToString(row["extra_data"])) ? Convert.ToString(row["extra_data"]) : "";
            if (present.Definition.Type == ItemType.Floor)
            {
                if (!room.GetRoomItemHandler().SetFloorItem(session, present, present.GetX, present.GetY, present.Rotation, true, false, true))
                {
                    using (var dbClient = _database.GetQueryReactor())
                    {
                        dbClient.SetQuery("UPDATE `items` SET `room_id` = '0' WHERE `id` = @itemId LIMIT 1");
                        dbClient.AddParameter("itemId", present.Id);
                        dbClient.RunQuery();
                    }
                    itemIsInRoom = false;
                }
            }
            else
            {
                using (var dbClient = _database.GetQueryReactor())
                {
                    dbClient.SetQuery("UPDATE `items` SET `room_id` = '0' WHERE `id` = @itemId LIMIT 1");
                    dbClient.AddParameter("itemId", present.Id);
                    dbClient.RunQuery();
                }
                itemIsInRoom = false;
            }
            session.Send(new OpenGiftComposer(present.Definition, present.LegacyDataString, present, itemIsInRoom));
            session.Send(new FurniListUpdateComposer());
        }
        catch
        {
            //ignored
        }
    }
}