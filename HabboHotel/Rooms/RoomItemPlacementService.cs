using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;

namespace Plus.HabboHotel.Rooms;

public interface IRoomItemPlacementService
{
    void Place(Room room, GameClient session, string rawData);
    void Move(Room room, GameClient session, uint itemId, int x, int y, int rotation);
}

public sealed class RoomItemPlacementService(ISettingsManager settings, IAchievementManager achievements,
    IRewardTrackManager rewardTracks, IQuestManager quests, ILogger<RoomItemPlacementService> logger) : IRoomItemPlacementService
{
    public void Place(Room room, GameClient session, string rawData)
    {
        var data = rawData.Split(' ');
        if (!uint.TryParse(data[0], out var itemId))
            return;
        var hasRights = room.CheckRights(session, false, true);
        if (!hasRights)
        {
            session.Send(new RoomNotificationComposer("furni_placement_error", "message", "${room.error.cant_set_not_owner}"));
            return;
        }
        if (room.GetRoomItemHandler().GetWallAndFloor.Count() > Convert.ToInt32(settings.TryGetValue("room.item.placement_limit")))
        {
            session.SendNotification($"You cannot have more than {Convert.ToInt32(settings.TryGetValue("room.item.placement_limit"))} items in a room!");
            return;
        }
        var inventoryItem = session.GetHabbo().Inventory.Furniture.GetItem(itemId);
        if (inventoryItem == null)
            return;
        var item = inventoryItem.ToRoomObject(session.GetHabbo());

        if (item.Definition.InteractionType == InteractionType.Exchange && room.OwnerId != session.GetHabbo().Id && !session.GetHabbo().Access.Can(PermissionKeys.RoomItemPlaceExchangeAnywhere))
        {
            session.SendNotification("You cannot place exchange items in other people's rooms!");
            return;
        }

        //TODO: Make neat.
        switch (item.Definition.InteractionType)
        {
            case InteractionType.Moodlight:
            {
                var moodData = room.MoodlightData;
                if (moodData != null && room.GetRoomItemHandler().GetItem(moodData.ItemId) != null)
                {
                    session.SendNotification("You can only have one background moodlight per room!");
                    return;
                }
                break;
            }
            case InteractionType.Toner:
            {
                var tonerData = room.TonerData;
                if (tonerData != null && room.GetRoomItemHandler().GetItem(tonerData.ItemId) != null)
                {
                    session.SendNotification("You can only have one background toner per room!");
                    return;
                }
                break;
            }
            case InteractionType.Hopper:
            {
                if (room.GetRoomItemHandler().HopperCount > 0)
                {
                    session.SendNotification("You can only have one hopper per room!");
                    return;
                }
                break;
            }
            case InteractionType.Tent:
            case InteractionType.TentSmall:
            {
                room.AddTent(item.Id);
                break;
            }
        }
        if (!item.IsWallItem)
        {
            if (data.Length < 4)
                return;
            if (!int.TryParse(data[1], out var x)) return;
            if (!int.TryParse(data[2], out var y)) return;
            if (!int.TryParse(data[3], out var rotation)) return;
            if (room.GetRoomItemHandler().SetFloorItem(session, item, x, y, rotation, true, false, true))
            {
                session.GetHabbo().Inventory.Furniture.RemoveItem(itemId);
                session.Send(new FurniListRemoveComposer(itemId));
                if (session.GetHabbo().Id == room.OwnerId)
                    achievements.ProgressAchievement(session, "ACH_RoomDecoFurniCount", 1);
                rewardTracks.Progress(session, RewardTrackActions.PlaceItem);
                if (item.IsWired)
                {
                    try
                    {
                        room.GetWired().LoadWiredBox(item);
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "Failed to load placed Wired item {ItemId} in room {RoomId}", item.Id, room.Id);
                    }
                }
            }
            else
                session.Send(new RoomNotificationComposer("furni_placement_error", "message", "${room.error.cant_set_item}"));
        }
        else if (item.IsWallItem)
        {
            var correctedData = new string[data.Length - 1];
            for (var i = 1; i < data.Length; i++) correctedData[i - 1] = data[i];
            var wallPos = room.GetRoomItemHandler().WallPositionCheck(string.Join(" ", correctedData));
            if (wallPos != null)
            {
                item.WallCoordinates = wallPos;
                try
                {
                    if (room.GetRoomItemHandler().SetWallItem(session, item))
                    {
                        session.GetHabbo().Inventory.Furniture.RemoveItem(itemId);
                        session.Send(new FurniListRemoveComposer(itemId));
                        if (session.GetHabbo().Id == room.OwnerId)
                            achievements.ProgressAchievement(session, "ACH_RoomDecoFurniCount", 1);
                        rewardTracks.Progress(session, RewardTrackActions.PlaceItem);
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to place wall item {ItemId} in room {RoomId}", item.Id, room.Id);
                    session.Send(new RoomNotificationComposer("furni_placement_error", "message", "${room.error.cant_set_item}"));
                }
            }
            else
                session.Send(new RoomNotificationComposer("furni_placement_error", "message", "${room.error.cant_set_item}"));
        }
    }
    public void Move(Room room, GameClient session, uint itemId, int x, int y, int rotation)
    {
        if (itemId == 0)
            return;
        Item item;
        if (room.Group != null)
        {
            if (!room.CheckRights(session, false, true))
            {
                item = room.GetRoomItemHandler().GetItem(itemId);
                if (item == null || item.IsTemporary)
                    return;
                session.Send(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));
                return;
            }
        }
        else
        {
            if (!room.CheckRights(session)) return;
        }
        item = room.GetRoomItemHandler().GetItem(itemId);
        if (item == null || item.IsTemporary)
            return;
        var moved = x != item.GetX || y != item.GetY;
        var rotated = rotation != item.Rotation;
        if (moved)
            quests.ProgressUserQuest(session, QuestType.FurniMove);
        if (rotated)
            quests.ProgressUserQuest(session, QuestType.FurniRotate);
        if (!room.GetRoomItemHandler().SetFloorItem(session, item, x, y, rotation, false, false, true))
        {
            room.SendPacket(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));
            return;
        }
        if (moved)
            rewardTracks.Progress(session, RewardTrackActions.MoveItem);
        if (rotated)
            rewardTracks.Progress(session, RewardTrackActions.RotateItem);
        if (item.GetZ >= 0.1)
            quests.ProgressUserQuest(session, QuestType.FurniStack);
    }

}
