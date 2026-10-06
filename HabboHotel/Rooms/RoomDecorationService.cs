using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Quests;

namespace Plus.HabboHotel.Rooms;

public enum RoomDecorationKind
{
    Floor, Wallpaper, Landscape
}
public readonly record struct ApplyRoomDecorationRequest(uint ItemId);

public interface IRoomDecorationStore
{
    void Apply(uint roomId, uint itemId, int userId, RoomDecorationKind kind, string data);
}

public sealed class RoomDecorationStore(IDatabase database) : IRoomDecorationStore
{
    public void Apply(uint roomId, uint itemId, int userId, RoomDecorationKind kind, string data)
    {
        var column = kind switch
        {
            RoomDecorationKind.Floor => "floor",
            RoomDecorationKind.Wallpaper => "wallpaper",
            RoomDecorationKind.Landscape => "landscape",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.Execute($"UPDATE rooms SET `{column}`=@data WHERE id=@roomId LIMIT 1", new { roomId, data }, transaction) != 1 ||
            connection.Execute("DELETE FROM items WHERE id=@itemId AND user_id=@userId AND room_id=0 LIMIT 1",
                new { itemId, userId }, transaction) != 1) {
            throw new InvalidOperationException("Room decoration was not persisted.");
        }

        transaction.Commit();
    }
}

public interface IRoomDecorationService
{
    void Apply(Room room, GameClient session, ApplyRoomDecorationRequest request);
}

public sealed class RoomDecorationService(IRoomDecorationStore store, IAchievementManager achievements, IQuestManager quests) : IRoomDecorationService
{
    public void Apply(Room room, GameClient session, ApplyRoomDecorationRequest request)
    {
        var habbo = session.GetHabbo();

        if (habbo.CurrentRoom != room || !room.CheckRights(session, true)) {
            return;
        }

        lock (habbo.Inventory.Furniture) {
            var item = habbo.Inventory.Furniture.GetItem(request.ItemId);

            if (item?.Definition == null || item.OwnerId != habbo.Id) {
                return;
            }

            var kind = item.Definition.InteractionType switch
            {
                InteractionType.Floor => RoomDecorationKind.Floor,
                InteractionType.Wallpaper => RoomDecorationKind.Wallpaper,
                InteractionType.Landscape => RoomDecorationKind.Landscape,
                _ => (RoomDecorationKind?)null
            };
            var data = item.ExtraData is LegacyDataFormat legacy ? legacy.Data : string.Empty;

            if (kind == null || string.IsNullOrWhiteSpace(data)) {
                return;
            }

            store.Apply(room.RoomId, item.Id, habbo.Id, kind.Value, data);

            switch (kind.Value) {
                case RoomDecorationKind.Floor:
                    room.Floor = data;
                    quests.ProgressUserQuest(session, QuestType.FurniDecoFloor);
                    achievements.ProgressAchievement(session, "ACH_RoomDecoFloor", 1);
                    break;
                case RoomDecorationKind.Wallpaper:
                    room.Wallpaper = data;
                    quests.ProgressUserQuest(session, QuestType.FurniDecoWall);
                    achievements.ProgressAchievement(session, "ACH_RoomDecoWallpaper", 1);
                    break;
                case RoomDecorationKind.Landscape:
                    room.Landscape = data;
                    achievements.ProgressAchievement(session, "ACH_RoomDecoLandscape", 1);
                    break;
            }

            habbo.Inventory.Furniture.RemoveItem(item.Id);
            session.Send(new FurniListRemoveComposer(item.Id));
            room.SendPacket(new RoomPropertyComposer(kind.Value.ToString().ToLowerInvariant(), data));
        }
    }
}
