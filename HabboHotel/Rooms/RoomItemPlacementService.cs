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
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Rooms;

public interface IRoomItemPlacementService
{
    void Place(Room room, GameClient session, string rawData);
    void Move(Room room, GameClient session, uint itemId, int x, int y, int rotation);
    void MoveWall(Room room, GameClient session, uint itemId, string location);
    void PlaceSticky(Room room, GameClient session, uint itemId, string location);
}

public sealed class RoomItemPlacementService(ISettingsManager settings, IAchievementManager achievements,
    IRewardTrackManager rewardTracks, IQuestManager quests, ILogger<RoomItemPlacementService> logger) : IRoomItemPlacementService
{
    public void Place(Room room, GameClient session, string rawData)
    {
        var data = rawData.Split(' ');

        if (!uint.TryParse(data[0], out var itemId)) {
            return;
        }

        var hasRights = room.CheckRights(session, false, true);

        if (!hasRights) {
            session.Send(new RoomNotificationComposer("furni_placement_error", "message", "${room.error.cant_set_not_owner}"));

            return;
        }

        if (room.GetRoomItemHandler().GetWallAndFloor.Count() > Convert.ToInt32(settings.TryGetValue("room.item.placement_limit"))) {
            session.SendNotification($"You cannot have more than {Convert.ToInt32(settings.TryGetValue("room.item.placement_limit"))} items in a room!");

            return;
        }

        // Placed furniture comes out of the loaded inventory, so nothing is placed without one.
        var inventory = session.GetHabbo().Inventory;
        var inventoryItem = inventory?.Furniture.GetItem(itemId);

        if (inventory == null || inventoryItem == null) {
            return;
        }

        if (!inventoryItem.TryReserve()) {
            return;
        }

        try {
            var habbo = session.GetHabbo();

            if (habbo.WalletClosed || !ReferenceEquals(inventory.Furniture.GetItem(itemId), inventoryItem)) {
                return;
            }

            PlaceReserved(room, session, data, inventory, inventoryItem);
        }
        finally {
            inventoryItem.ReleaseReservation();
        }
    }

    private void PlaceReserved(Room room, GameClient session, string[] data, InventoryComponent inventory, InventoryItem inventoryItem)
    {
        if (!Music.RoomMusicDefinition.IsPlayer(inventoryItem.Definition)) {
            PlaceReservedCore(room, session, data, inventory, inventoryItem);

            return;
        }

        if (!room.Music.TryBeginPlacement()) {
            return;
        }

        try {
            PlaceReservedCore(room, session, data, inventory, inventoryItem);
        }
        finally {
            room.Music.EndPlacement();
        }
    }

    private void PlaceReservedCore(Room room, GameClient session, string[] data, InventoryComponent inventory, InventoryItem inventoryItem)
    {
        var itemId = inventoryItem.Id;
        var item = inventoryItem.ToRoomObject(session.GetHabbo());

        if (item.Definition.InteractionType == InteractionType.Exchange && room.OwnerId != session.GetHabbo().Id && !session.GetHabbo().Access.Can(PermissionKeys.RoomItemPlaceExchangeAnywhere)) {
            session.SendNotification("You cannot place exchange items in other people's rooms!");

            return;
        }

        //TODO: Make neat.
        switch (item.Definition.InteractionType) {
            case InteractionType.Moodlight: {
                    var moodData = room.MoodlightData;

                    if (moodData != null && room.GetRoomItemHandler().GetItem(moodData.ItemId) != null) {
                        session.SendNotification("You can only have one background moodlight per room!");

                        return;
                    }

                    break;
                }
            case InteractionType.Toner: {
                    var tonerData = room.TonerData;

                    if (tonerData != null && room.GetRoomItemHandler().GetItem(tonerData.ItemId) != null) {
                        session.SendNotification("You can only have one background toner per room!");

                        return;
                    }

                    break;
                }
            case InteractionType.Hopper: {
                    if (room.GetRoomItemHandler().HopperCount > 0) {
                        session.SendNotification("You can only have one hopper per room!");

                        return;
                    }

                    break;
                }
            case InteractionType.Tent:
            case InteractionType.TentSmall: {
                    room.AddTent(item.Id);
                    break;
                }
        }

        if (!item.IsWallItem) {
            if (data.Length < 4) {
                return;
            }

            if (!int.TryParse(data[1], out var x)) {
                return;
            }

            if (!int.TryParse(data[2], out var y)) {
                return;
            }

            if (!int.TryParse(data[3], out var rotation)) {
                return;
            }

            if (room.GetRoomItemHandler().SetFloorItem(session, item, x, y, rotation, true, false, true, height: BuildHeight(room, session, item, x, y, rotation))) {
                inventory.Furniture.RemoveItem(itemId);
                session.Send(new FurniListRemoveComposer(itemId));

                if (session.GetHabbo().Id == room.OwnerId) {
                    achievements.ProgressAchievement(session, "ACH_RoomDecoFurniCount", 1);
                }

                rewardTracks.Progress(session, RewardTrackActions.PlaceItem);

                if (item.IsWired) {
                    try {
                        room.GetWired().LoadWiredBox(item);
                    }
                    catch (Exception exception) {
                        logger.LogError(exception, "Failed to load placed Wired item {ItemId} in room {RoomId}", item.Id, room.Id);
                    }
                }
            }
            else {
                session.Send(new RoomNotificationComposer("furni_placement_error", "message", "${room.error.cant_set_item}"));
            }
        }
        else if (item.IsWallItem) {
            var correctedData = new string[data.Length - 1];

            for (var i = 1; i < data.Length; i++) {
                correctedData[i - 1] = data[i];
            }

            var wallPos = room.GetRoomItemHandler().WallPositionCheck(string.Join(" ", correctedData));

            if (wallPos != null) {
                item.WallCoordinates = wallPos;

                try {
                    if (room.GetRoomItemHandler().SetWallItem(session, item)) {
                        inventory.Furniture.RemoveItem(itemId);
                        session.Send(new FurniListRemoveComposer(itemId));

                        if (session.GetHabbo().Id == room.OwnerId) {
                            achievements.ProgressAchievement(session, "ACH_RoomDecoFurniCount", 1);
                        }

                        rewardTracks.Progress(session, RewardTrackActions.PlaceItem);
                    }
                }
                catch (Exception exception) {
                    logger.LogError(exception, "Failed to place wall item {ItemId} in room {RoomId}", item.Id, room.Id);
                    session.Send(new RoomNotificationComposer("furni_placement_error", "message", "${room.error.cant_set_item}"));
                }
            }
            else {
                session.Send(new RoomNotificationComposer("furni_placement_error", "message", "${room.error.cant_set_item}"));
            }
        }
    }
    public void MoveWall(Room room, GameClient session, uint itemId, string location)
    {
        if (!ReferenceEquals(session.GetHabbo().CurrentRoom, room) || !room.CheckRights(session)) {
            return;
        }

        var item = room.GetRoomItemHandler().GetItem(itemId);

        if (item == null || item.IsTemporary) {
            return;
        }

        try {
            item.WallCoordinates = room.GetRoomItemHandler().WallPositionCheck($":{location.Split(':')[1]}");
        }
        catch (Exception exception) {
            logger.LogWarning(exception, "Invalid wall move for {ItemId} in {RoomId}", itemId, room.Id);

            return;
        }

        room.GetRoomItemHandler().UpdateItem(item);
        room.SendPacket(new ItemUpdateComposer(RoomItemSnapshot.Capture(item)));
    }

    public void PlaceSticky(Room room, GameClient session, uint itemId, string location)
    {
        var habbo = session.GetHabbo();

        if (!ReferenceEquals(habbo.CurrentRoom, room) || !room.CheckRights(session)) {
            return;
        }

        // The sticky note comes out of the loaded inventory, so nothing is placed without one.
        if (habbo.Inventory is not { } inventory || inventory.Furniture.GetItem(itemId) is not { } item) {
            return;
        }

        if (!item.TryReserve()) {
            return;
        }

        try {
            if (habbo.WalletClosed || !ReferenceEquals(inventory.Furniture.GetItem(itemId), item)) {
                return;
            }

            var position = room.GetRoomItemHandler().WallPositionCheck($":{location.Split(':')[1]}");
            var placed = item.ToRoomObject(habbo);
            placed.WallCoordinates = position;

            if (room.GetRoomItemHandler().SetWallItem(session, placed)) {
                inventory.Furniture.RemoveItem(itemId);
                session.Send(new FurniListRemoveComposer(itemId));
            }
        }
        catch (Exception exception) {
            logger.LogError(exception, "Unable to place sticky {ItemId} for {UserId} in {RoomId}", itemId, habbo.Id, room.Id);
        }
        finally {
            item.ReleaseReservation();
        }
    }

    public void Move(Room room, GameClient session, uint itemId, int x, int y, int rotation)
    {
        if (itemId == 0) {
            return;
        }

        Item item;

        if (room.Group != null) {
            if (!room.CheckRights(session, false, true)) {
                item = room.GetRoomItemHandler().GetItem(itemId);

                if (item == null || item.IsTemporary) {
                    return;
                }

                session.Send(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));

                return;
            }
        }
        else {
            if (!room.CheckRights(session)) {
                return;
            }
        }

        item = room.GetRoomItemHandler().GetItem(itemId);

        if (item == null || item.IsTemporary) {
            return;
        }

        var moved = x != item.GetX || y != item.GetY;
        var rotated = rotation != item.Rotation;

        if (moved) {
            quests.ProgressUserQuest(session, QuestType.FurniMove);
        }

        if (rotated) {
            quests.ProgressUserQuest(session, QuestType.FurniRotate);
        }

        if (!room.GetRoomItemHandler().SetFloorItem(session, item, x, y, rotation, false, false, true, height: BuildHeight(room, session, item, x, y, rotation))) {
            room.SendPacket(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));

            return;
        }

        if (moved) {
            rewardTracks.Progress(session, RewardTrackActions.MoveItem);
        }

        if (rotated) {
            rewardTracks.Progress(session, RewardTrackActions.RotateItem);
        }

        if (item.GetZ >= 0.1) {
            quests.ProgressUserQuest(session, QuestType.FurniStack);
        }
    }

    // The :bh height, never below the floor under the item's footprint; -1 keeps normal stacking.
    private static double BuildHeight(Room room, GameClient session, Item item, int x, int y, int rotation)
    {
        if (room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id)?.BuildHeight is not { } height) {
            return -1;
        }

        var map = room.GetGameMap();
        var footprint = Gamemap.GetAffectedTiles(item.Definition.Length, item.Definition.Width, x, y, rotation).Values
            .Select(tile => (X: tile.X, Y: tile.Y)).Append((X: x, Y: y)).ToArray();

        // An off-map tile is refused by the placement itself.
        if (footprint.Any(tile => !map.ValidTile(tile.X, tile.Y))) {
            return -1;
        }

        return MagicTileHeight.Clamp(height, footprint.Max(tile => (double)map.Model.SqFloorHeight[tile.X, tile.Y]));
    }

}
