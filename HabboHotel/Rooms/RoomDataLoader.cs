using System.Diagnostics.CodeAnalysis;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Groups;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Singleton]
public interface IRoomDataLoader
{
    bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data);
    List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId);
}

public sealed class RoomDataLoader(IDatabase database, IRoomManager rooms, IGroupManager groups,
    IRoomPromotionLoader promotions) : IRoomDataLoader
{
    public bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data)
    {
        if (rooms.TryGetRoom(roomId, out var loaded))
        {
            data = loaded.Data;
            return true;
        }

        using var connection = database.Connection();
        var row = connection.QuerySingleOrDefault<RoomDataRow>(RoomDataMapping.SelectRoom +
            "WHERE rooms.id = @roomId LIMIT 1", new { roomId });
        if (row == null || !rooms.TryGetModel(row.ModelName, out var model))
        {
            data = null;
            return false;
        }

        data = Prepare(RoomDataMapping.CreateData(row, model, true));
        return true;
    }

    public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId)
    {
        using var connection = database.Connection();
        var rows = connection.Query<RoomDataRow>(RoomDataMapping.SelectRoom +
            "WHERE rooms.owner = @ownerId ORDER BY rooms.caption", new { ownerId });
        var data = new List<RoomData>();
        foreach (var row in rows)
        {
            if (rooms.TryGetRoom(row.Id, out var loaded))
                data.Add(loaded.Data);
            else if (rooms.TryGetModel(row.ModelName, out var model))
                data.Add(Prepare(RoomDataMapping.CreateData(row, model, false)));
        }
        return data;
    }

    private RoomData Prepare(RoomData data)
    {
        data.Promotion = promotions.Load(data.Id);
        if (data.GroupId > 0 && groups.TryGetGroup(data.GroupId, out var group))
            data.Group = group;
        return data;
    }
}

internal static class RoomDataMapping
{
    internal const string SelectRoom = "SELECT rooms.`allow_pets` AS AllowPets, rooms.`allow_pets_eat` AS AllowPetsEat, rooms.`room_blocking_disabled` AS RoomBlockingDisabled, rooms.`allow_hidewall` AS AllowHidewall, rooms.`push_enabled` AS PushEnabled, rooms.`pull_enabled` AS PullEnabled, rooms.`spush_enabled` AS SpushEnabled, rooms.`spull_enabled` AS SpullEnabled, rooms.`enables_enabled` AS EnablesEnabled, rooms.`respect_notifications_enabled` AS RespectNotificationsEnabled, rooms.`pet_morphs_allowed` AS PetMorphsAllowed, rooms.`lay_enabled` AS LayEnabled, rooms.`id` AS Id, rooms.`caption` AS Caption, rooms.`model_name` AS ModelName, users.`username` AS Username, rooms.`owner` AS Owner, rooms.`password` AS Password, rooms.`score` AS Score, rooms.`roomtype` AS Roomtype, rooms.`state` AS State, rooms.`users_now` AS UsersNow, rooms.`users_max` AS UsersMax, rooms.`category` AS Category, rooms.`description` AS Description, rooms.`tags` AS Tags, rooms.`floor` AS Floor, rooms.`landscape` AS Landscape, rooms.`wallthick` AS Wallthick, rooms.`floorthick` AS Floorthick, rooms.`wallpaper` AS Wallpaper, rooms.`mute_settings` AS MuteSettings, rooms.`ban_settings` AS BanSettings, rooms.`kick_settings` AS KickSettings, rooms.`chat_mode` AS ChatMode, rooms.`chat_size` AS ChatSize, rooms.`chat_speed` AS ChatSpeed, rooms.`chat_extra_flood` AS ChatExtraFlood, rooms.`chat_hearing_distance` AS ChatHearingDistance, rooms.`trade_settings` AS TradeSettings, rooms.`group_id` AS GroupId, rooms.`sale_price` AS SalePrice FROM rooms LEFT JOIN users ON rooms.owner = users.id ";

    internal static RoomData CreateData(RoomDataRow row, RoomModel model, bool fallbackOwnerName) => new(
        row.Id, row.Caption, row.ModelName,
        fallbackOwnerName && string.IsNullOrEmpty(row.Username) ? "Habboon" : row.Username ?? string.Empty,
        row.Owner, row.Password, row.Score, row.Roomtype, row.State, row.UsersNow, row.UsersMax, row.Category,
        row.Description, row.Tags, row.Floor, row.Landscape, row.AllowPets, row.AllowPetsEat,
        row.RoomBlockingDisabled, row.AllowHidewall, row.Wallthick, row.Floorthick, row.Wallpaper,
        row.MuteSettings, row.BanSettings, row.KickSettings, row.ChatMode, row.ChatSize, row.ChatSpeed,
        row.ChatExtraFlood, row.ChatHearingDistance, row.TradeSettings, row.PushEnabled, row.PullEnabled,
        row.SpushEnabled, row.SpullEnabled, row.EnablesEnabled, row.RespectNotificationsEnabled,
        row.PetMorphsAllowed, row.GroupId, row.SalePrice, row.LayEnabled, model);
}

internal sealed class RoomDataRow
{
    public bool AllowPets { get; set; }
    public bool AllowPetsEat { get; set; }
    public bool RoomBlockingDisabled { get; set; }
    public bool AllowHidewall { get; set; }
    public bool PushEnabled { get; set; }
    public bool PullEnabled { get; set; }
    public bool SpushEnabled { get; set; }
    public bool SpullEnabled { get; set; }
    public bool EnablesEnabled { get; set; }
    public bool RespectNotificationsEnabled { get; set; }
    public bool PetMorphsAllowed { get; set; }
    public bool LayEnabled { get; set; }
    public uint Id { get; set; }
    public string Caption { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? Username { get; set; }
    public int Owner { get; set; }
    public string Password { get; set; } = string.Empty;
    public int Score { get; set; }
    public string Roomtype { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int UsersNow { get; set; }
    public int UsersMax { get; set; }
    public int Category { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public string Landscape { get; set; } = string.Empty;
    public int Wallthick { get; set; }
    public int Floorthick { get; set; }
    public string Wallpaper { get; set; } = string.Empty;
    public int MuteSettings { get; set; }
    public int BanSettings { get; set; }
    public int KickSettings { get; set; }
    public int ChatMode { get; set; }
    public int ChatSize { get; set; }
    public int ChatSpeed { get; set; }
    public int ChatExtraFlood { get; set; }
    public int ChatHearingDistance { get; set; }
    public int TradeSettings { get; set; }
    public int GroupId { get; set; }
    public int SalePrice { get; set; }
}
