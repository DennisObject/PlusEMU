using System.Collections.Immutable;
using Dapper;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users.Messenger;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

public readonly record struct PromotionRoomSnapshot(uint Id, string Name);

public sealed record PurchaseRoomPromotionRequest(uint RoomId, string Name, string Description, int CategoryId);
public sealed record EditRoomPromotionRequest(uint RoomId, string Name, string Description);

public interface IRoomPromotionStore
{
    void Save(uint roomId, int ownerId, RoomPromotion promotion);
    void Edit(uint roomId, int ownerId, string name, string description);
}

public sealed class RoomPromotionStore(IDatabase database) : IRoomPromotionStore
{
    public void Save(uint roomId, int ownerId, RoomPromotion promotion)
    {
        using var connection = database.Connection();
        if (connection.Execute("""
            INSERT INTO room_promotions (room_id,title,description,timestamp_start,timestamp_expire,category_id)
            SELECT @roomId,@Name,@Description,@startedAt,@expiresAt,@CategoryId
            FROM rooms WHERE id=@roomId AND owner=@ownerId
            ON DUPLICATE KEY UPDATE title=VALUES(title),description=VALUES(description),
                timestamp_start=VALUES(timestamp_start),timestamp_expire=VALUES(timestamp_expire),category_id=VALUES(category_id)
            """, new { roomId, ownerId, promotion.Name, promotion.Description, promotion.CategoryId,
                startedAt = promotion.StartedAt?.UtcDateTime, expiresAt = promotion.ExpiresAt?.UtcDateTime }) == 0)
            throw new InvalidOperationException("Room promotion was not persisted.");
    }

    public void Edit(uint roomId, int ownerId, string name, string description)
    {
        using var connection = database.Connection();
        if (connection.Execute("""
            UPDATE room_promotions promotion JOIN rooms room ON room.id=promotion.room_id
            SET promotion.title=@name,promotion.description=@description
            WHERE promotion.room_id=@roomId AND room.owner=@ownerId
            """, new { roomId, ownerId, name, description }) != 1)
            throw new InvalidOperationException("Room promotion was not updated.");
    }
}

[Singleton]
public interface IRoomPromotionService
{
    void ShowCatalogRooms(GameClient session);
    Task Purchase(GameClient session, PurchaseRoomPromotionRequest request);
    void Edit(GameClient session, EditRoomPromotionRequest request);
}

public sealed class RoomPromotionService(IRoomDataLoader dataLoader, IRoomManager rooms, IRoomPromotionStore store,
    IWordFilterManager filter, ISettingsManager settings, IBadgeManager badges, IMessengerDataLoader messenger,
    TimeProvider clock) : IRoomPromotionService
{
    private readonly object _sync = new();

    public void ShowCatalogRooms(GameClient session) =>
        session.Send(new GetCatalogRoomPromotionComposer(dataLoader.GetRoomsDataByOwnerSortByName(session.GetHabbo().Id)
            .Select(room => new PromotionRoomSnapshot(room.Id, room.Name)).ToImmutableArray()));

    public async Task Purchase(GameClient session, PurchaseRoomPromotionRequest request)
    {
        var habbo = session.GetHabbo();
        RoomData data;
        RoomEventSnapshot snapshot;
        string name;
        lock (_sync)
        {
            if (!dataLoader.TryGetData(request.RoomId, out var loaded) || loaded.OwnerId != habbo.Id)
                return;
            data = loaded;
            name = filter.CheckMessage(request.Name);
            var description = filter.CheckMessage(request.Description);
            var now = clock.GetUtcNow();
            var previous = data.Promotion;
            var startedAt = previous == null ? now : previous.StartedAt;
            var expiresAt = previous == null
                ? now.AddMinutes(Convert.ToInt32(settings.TryGetValue("room.promotion.lifespan")))
                : (previous.ExpiresAt ?? now).AddHours(2);
            var promotion = new RoomPromotion(name, description, startedAt, expiresAt, request.CategoryId, clock);
            store.Save(data.Id, habbo.Id, promotion);
            data.Promotion = promotion;
            snapshot = RoomEventSnapshot.Capture(data, promotion);
        }
        if (!habbo.Inventory.Badges.HasBadge("RADZZ")) await badges.GiveBadge(habbo, "RADZZ");
        session.Send(new PurchaseOKComposer());
        if (habbo.CurrentRoom is { } currentRoom && currentRoom.Id == data.Id)
            currentRoom.SendPacket(new RoomEventComposer(snapshot));
        messenger.BroadcastStatusUpdate(habbo, MessengerEventTypes.EventStarted, name);
    }

    public void Edit(GameClient session, EditRoomPromotionRequest request)
    {
        var habbo = session.GetHabbo();
        lock (_sync)
        {
            if (!dataLoader.TryGetData(request.RoomId, out var data) || data.OwnerId != habbo.Id)
                return;
            if (data.Promotion is not { } promotion)
            {
                session.SendNotification("Oops, it looks like there isn't a room promotion in this room?");
                return;
            }
            var name = filter.CheckMessage(request.Name);
            var description = filter.CheckMessage(request.Description);
            store.Edit(data.Id, habbo.Id, name, description);
            promotion.Name = name;
            promotion.Description = description;
            if (rooms.TryGetRoom(data.Id, out var room))
                room.SendPacket(new RoomEventComposer(RoomEventSnapshot.Capture(data, promotion)));
        }
    }
}
