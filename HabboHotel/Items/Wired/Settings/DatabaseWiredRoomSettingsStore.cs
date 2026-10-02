using System.Data;
using System.Globalization;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Items.Wired.Settings;

/// <summary>Room owner and the expected companion row are checked in the transaction that stores settings.</summary>
public sealed class DatabaseWiredRoomSettingsStore(IDatabase database) : IWiredRoomSettingsStore
{
    private const string Select = "SELECT inspect_mask AS InspectMask, modify_mask AS ModifyMask, timezone AS TimeZoneId "
        + "FROM room_wired_settings WHERE room_id=@Id";

    public WiredRoomSettingsSnapshot? Load(uint roomId)
    {
        using var connection = database.Connection();
        return Read(connection, roomId);
    }

    public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings)
    {
        if (roomId == 0 || actorId <= 0 || !WiredRoomSettingsSnapshot.TryValidate(settings.InspectMask, settings.ModifyMask, settings.TimeZoneId, out var validated))
            throw new ArgumentException("Invalid Wired room settings.");
        settings = validated;
        using var connection = database.Connection();
        if (connection.State != ConnectionState.Open) connection.Open();
        using var transaction = connection.BeginTransaction();
        var owner = connection.QuerySingleOrDefault<string>("SELECT owner FROM rooms WHERE id=@Id FOR UPDATE", new { Id = roomId }, transaction);
        if (owner == null || !int.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId)
            || ownerId <= 0 || !staff && ownerId != actorId)
            throw new InvalidOperationException("Room ownership changed or settings access was denied.");
        var current = Read(connection, roomId, transaction, true);
        if (current != expected)
            throw new InvalidOperationException("Wired room settings changed; reload before saving.");
        connection.Execute("INSERT INTO room_wired_settings (room_id,inspect_mask,modify_mask,timezone) VALUES (@Id,@InspectMask,@ModifyMask,@TimeZoneId) "
            + "ON DUPLICATE KEY UPDATE inspect_mask=@InspectMask,modify_mask=@ModifyMask,timezone=@TimeZoneId",
            new { Id = roomId, settings.InspectMask, settings.ModifyMask, settings.TimeZoneId }, transaction);
        transaction.Commit();
    }

    private static WiredRoomSettingsSnapshot? Read(IDbConnection connection, uint id, IDbTransaction? transaction = null, bool forUpdate = false)
    {
        var row = connection.QuerySingleOrDefault<Row>(Select + (forUpdate ? " FOR UPDATE" : ""), new { Id = id }, transaction);
        if (row == null) return null;
        if (!WiredRoomSettingsSnapshot.TryValidate(row.InspectMask, row.ModifyMask, row.TimeZoneId, out var settings))
            throw new InvalidDataException("Invalid stored Wired room settings.");
        return settings;
    }

    private sealed class Row
    {
        public int InspectMask { get; set; }
        public int ModifyMask { get; set; }
        public string TimeZoneId { get; set; } = "";
    }
}
