using System.Data;
using System.Text.Json;
using Dapper;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Camera;

public sealed record CameraCheckoutMedia(Guid Id, uint RoomId, DateTimeOffset CreatedAt);
public sealed record CameraCheckoutResult(bool Ok, string Error = "", int WaitSeconds = 0, InventoryItem? Item = null, bool Changed = false);
[Singleton]
public interface ICameraCheckoutService
{
    bool Enabled { get; }
    (int Credits, int Points, int PublishPoints) Prices { get; }
    CameraCheckoutResult Purchase(Habbo habbo, CameraCheckoutMedia media);
    CameraCheckoutResult Publish(Habbo habbo, CameraCheckoutMedia media);
    CameraCheckoutResult EnterCompetition(Habbo habbo, CameraCheckoutMedia media);
}

public sealed class CameraCheckoutService(IDatabase database, ISettingsManager settings, IItemDataManager definitions, TimeProvider time) : ICameraCheckoutService
{
    public bool Enabled => settings.TryGetValue("camera.enabled") == "1";
    public (int Credits, int Points, int PublishPoints) Prices =>
        (Setting("camera.price.credits"), Setting("camera.price.points"), Setting("camera.price.publish.points"));
    private int Setting(string key)
    {
        if (!int.TryParse(settings.TryGetValue(key), out int value) || value < 0 || value > 1_000_000)
            throw new InvalidOperationException($"Invalid {key}");
        return value;
    }
    private bool HasContext(Habbo habbo, CameraCheckoutMedia media) =>
        Enabled && !habbo.WalletClosed && habbo.CurrentRoom?.RoomId == media.RoomId && media.Id != Guid.Empty;
    private static bool Owned(IDbConnection connection, IDbTransaction transaction, Habbo habbo, CameraCheckoutMedia media) =>
        connection.QuerySingleOrDefault<int?>("SELECT user_id FROM camera_media WHERE id=@id AND room_id=@roomId FOR UPDATE",
            new { id = media.Id.ToString("D"), roomId = media.RoomId }, transaction) == habbo.Id;
    private static void LockUser(IDbConnection connection, IDbTransaction transaction, int userId)
    {
        if (connection.QuerySingleOrDefault<int?>("SELECT id FROM users WHERE id=@userId FOR UPDATE", new { userId }, transaction) != userId)
            throw new InvalidOperationException("Camera account does not exist");
    }
    // The existing camera protocol expresses both point prices in duckets.
    private bool ValidCurrency(string key) => Setting(key) == 0;

    public CameraCheckoutResult Purchase(Habbo habbo, CameraCheckoutMedia media)
    {
        lock (habbo.WalletSync)
        {
            if (!HasContext(habbo, media)) return new(false, "unavailable");
            int credits = Setting("camera.price.credits"), points = Setting("camera.price.points");
            if (!uint.TryParse(settings.TryGetValue("camera.item_id"), out uint definitionId)) return new(false, "unavailable");
            if (!ValidCurrency("camera.price.points.type") || !definitions.Items.TryGetValue(definitionId, out var definition) ||
                definition.Type != ItemType.Wall || definition.InteractionType != InteractionType.CameraPicture) return new(false, "unavailable");
            if (habbo.Credits < credits || habbo.Duckets < points) return new(false, "insufficient_balance");
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            LockUser(connection, transaction, habbo.Id);
            if (!Owned(connection, transaction, habbo, media)) return new(false, "unavailable");
            string extraData = JsonSerializer.Serialize(new { w = CameraMediaPath.For(media.Id), t = media.CreatedAt.ToUnixTimeSeconds(),
                o = habbo.Username, oi = habbo.Id, s = media.RoomId, n = "", m = "" });
            uint itemId = connection.QuerySingle<uint>("""
                INSERT INTO items (user_id,room_id,base_item,extra_data,x,y,z,rot,wall_pos)
                VALUES (@userId,0,@definitionId,@extraData,0,0,0,0,''); SELECT LAST_INSERT_ID();
                """, new { userId = habbo.Id, definitionId, extraData }, transaction);
            connection.Execute("INSERT INTO camera_purchases (item_id,media_id,user_id,created_at) VALUES (@itemId,@id,@userId,@now)",
                new { itemId, id = media.Id.ToString("D"), userId = habbo.Id, now = time.GetUtcNow().UtcDateTime }, transaction);
            Charge(connection, transaction, habbo, credits, points);
            transaction.Commit();
            habbo.Credits -= credits;
            habbo.Duckets -= points;
            return new(true, Item: new InventoryItem { Id = itemId, OwnerId = (uint)habbo.Id, Definition = definition,
                ExtraData = FurniExtraData.Load(definition, extraData, keepLegacy: true) }, Changed: true);
        }
    }

    public CameraCheckoutResult Publish(Habbo habbo, CameraCheckoutMedia media)
    {
        lock (habbo.WalletSync)
        {
            if (!HasContext(habbo, media) || !ValidCurrency("camera.price.publish.points.type")) return new(false, "unavailable");
            int price = Setting("camera.price.publish.points"), cooldown = Setting("camera.publish.cooldown");
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            LockUser(connection, transaction, habbo.Id);
            if (!Owned(connection, transaction, habbo, media)) return new(false, "unavailable");
            var args = new { id = media.Id.ToString("D"), userId = habbo.Id, roomId = media.RoomId, now = time.GetUtcNow().UtcDateTime };
            if (connection.QuerySingle<int>("SELECT COUNT(*) FROM camera_publications WHERE media_id=@id AND user_id=@userId", args, transaction) != 0) return new(true);
            connection.Execute("INSERT IGNORE INTO camera_accounts (user_id) VALUES (@userId)", args, transaction);
            var last = connection.QuerySingleOrDefault<long?>("SELECT TIMESTAMPDIFF(MICROSECOND,'1970-01-01',last_publish_at) FROM camera_accounts WHERE user_id=@userId FOR UPDATE", args, transaction);
            int wait = last.HasValue ? Math.Max(0, (int)Math.Ceiling((last.Value / 1_000_000d + cooldown - (args.now - DateTime.UnixEpoch).TotalSeconds))) : 0;
            if (wait > 0) return new(false, "cooldown", wait);
            if (habbo.Duckets < price) return new(false, "insufficient_balance");
            connection.Execute("INSERT INTO camera_publications (media_id,user_id,room_id,created_at) VALUES (@id,@userId,@roomId,@now)", args, transaction);
            connection.Execute("UPDATE camera_accounts SET last_publish_at=@now WHERE user_id=@userId", args, transaction);
            Charge(connection, transaction, habbo, 0, price);
            transaction.Commit();
            habbo.Duckets -= price;
            return new(true, Changed: true);
        }
    }

    public CameraCheckoutResult EnterCompetition(Habbo habbo, CameraCheckoutMedia media)
    {
        lock (habbo.WalletSync)
        {
            if (!HasContext(habbo, media) || settings.TryGetValue("camera.competition.enabled") != "1") return new(false, "disabled");
            // Plus has no verified-email flag, so fail closed when verification is required.
            if (settings.TryGetValue("camera.competition.require_email") != "0") return new(false, "email");
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            LockUser(connection, transaction, habbo.Id);
            if (!Owned(connection, transaction, habbo, media)) return new(false, "unavailable");
            var now = time.GetUtcNow().UtcDateTime;
            var args = new { id = media.Id.ToString("D"), userId = habbo.Id, now, day = now.Date };
            if (connection.QuerySingle<int>("SELECT COUNT(*) FROM camera_competition_entries WHERE media_id=@id AND user_id=@userId", args, transaction) != 0) return new(true);
            int count = connection.QuerySingle<int>("SELECT COUNT(*) FROM camera_competition_entries WHERE user_id=@userId AND created_at>=@day", args, transaction);
            if (count >= Setting("camera.competition.daily")) return new(false, "limit");
            connection.Execute("INSERT INTO camera_competition_entries (media_id,user_id,created_at) VALUES (@id,@userId,@now)", args, transaction);
            transaction.Commit();
            return new(true, Changed: true);
        }
    }
    private static void Charge(IDbConnection connection, IDbTransaction transaction, Habbo habbo, int credits, int points) =>
        connection.Execute("UPDATE users SET credits=@credits,activity_points=@points,vip_points=@diamonds WHERE id=@userId",
            new { credits = habbo.Credits - credits, points = habbo.Duckets - points, diamonds = habbo.Diamonds, userId = habbo.Id }, transaction);
}
