using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Recycler;

[Singleton]
public interface IRecyclerStore
{
    RecyclerConfiguration Load();
    DateTimeOffset? NextAllowed(int userId);
    RecycledBox? Recycle(int userId, uint boxDefinitionId, RecyclerConfiguration configuration, RecyclerPrize prize,
        IReadOnlyList<RecyclerInput> inputs, DateTimeOffset now);
    GiftContent? FindBox(uint itemId, int ownerId, uint roomId, uint boxDefinitionId);
    uint? OpenBox(uint itemId, int ownerId, uint roomId, uint boxDefinitionId, GiftContent content);
}

public sealed class RecyclerStore(IDatabase database) : IRecyclerStore
{
    public RecyclerConfiguration Load()
    {
        using var connection = database.Connection();
        return ReadConfiguration(connection);
    }

    public DateTimeOffset? NextAllowed(int userId)
    {
        using var connection = database.Connection();
        return connection.QuerySingleOrDefault<DateTimeOffset?>("SELECT next_allowed_at FROM user_recycler WHERE user_id=@userId", new { userId });
    }

    public RecycledBox? Recycle(int userId, uint boxDefinitionId, RecyclerConfiguration configuration, RecyclerPrize prize,
        IReadOnlyList<RecyclerInput> inputs, DateTimeOffset now)
    {
        if (!configuration.Enabled || inputs.Count != configuration.Slots || inputs.Count is < 1 or > 12
            || inputs.Any(input => input.Id == 0) || inputs.Select(input => input.Id).Distinct().Count() != inputs.Count) {
            return null;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id=@userId FOR UPDATE", new { userId }, transaction) is null
            || !configuration.Matches(ReadConfiguration(connection, transaction))) {
            return null;
        }
        // Create before the locking read: missing-row gap locks must not couple different users.
        connection.Execute("INSERT IGNORE INTO user_recycler(user_id,next_allowed_at) VALUES(@userId,@now)",
            new { userId, now = now.UtcDateTime }, transaction);
        var next = connection.QuerySingleOrDefault<DateTimeOffset?>(
            "SELECT next_allowed_at FROM user_recycler WHERE user_id=@userId FOR UPDATE", new { userId }, transaction);
        if (next > now || !configuration.Levels.SelectMany(level => level.Prizes).Contains(prize)) {
            return null;
        }
        if (connection.ExecuteScalar<uint?>("SELECT id FROM furniture WHERE id=@boxDefinitionId AND BINARY item_name=BINARY @name AND sprite_id=@sprite AND type='s' AND interaction_type IN ('','default') LOCK IN SHARE MODE",
                new { boxDefinitionId, name = RecyclerBox.ClassName, sprite = RecyclerBox.SpriteId }, transaction) is null
            || connection.ExecuteScalar<uint?>("SELECT id FROM furniture WHERE id=@id AND type IN ('s','i') AND sprite_id>0 LOCK IN SHARE MODE", new { id = prize.ItemId }, transaction) is null) {
            return null;
        }
        var ids = inputs.Select(input => input.Id).ToArray();
        var rows = connection.Query<InputRow>(
            "SELECT id,base_item AS ItemId,limited_number AS LimitedNumber,limited_stack AS LimitedStack FROM items WHERE id IN @ids AND user_id=@userId AND room_id=0 ORDER BY id FOR UPDATE",
            new { ids, userId }, transaction).ToArray();
        if (rows.Length != inputs.Count || rows.Any(row => row.LimitedNumber != 0 || row.LimitedStack != 0)
            || !rows.Select(row => new RecyclerInput(row.Id, row.ItemId)).SequenceEqual(inputs.OrderBy(input => input.Id))) {
            return null;
        }
        var definitionIds = rows.Select(row => row.ItemId).Distinct().ToArray();
        var eligible = connection.Query<uint>("SELECT id FROM furniture WHERE id IN @definitionIds AND allow_recycle=1 LOCK IN SHARE MODE",
            new { definitionIds }, transaction).ToHashSet();
        if (eligible.Count != definitionIds.Length) {
            return null;
        }
        if (connection.Execute("DELETE FROM items WHERE id IN @ids AND user_id=@userId AND room_id=0", new { ids, userId }, transaction) != inputs.Count) {
            throw new DBConcurrencyException("Recycler inputs changed during consumption.");
        }
        var extraData = now.UtcDateTime.ToString("d-M-yyyy", CultureInfo.InvariantCulture);
        connection.Execute("INSERT INTO items(base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data,limited_number,limited_stack) VALUES(@boxDefinitionId,@userId,0,0,0,0,'',0,@extraData,0,0)",
            new { boxDefinitionId, userId, extraData }, transaction);
        var id = connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()", transaction: transaction);
        connection.Execute("INSERT INTO user_presents(item_id,base_id,extra_data) VALUES(@id,@itemId,'')", new { id, itemId = prize.ItemId }, transaction);
        connection.Execute("INSERT INTO user_recycler(user_id,next_allowed_at) VALUES(@userId,@next) ON DUPLICATE KEY UPDATE next_allowed_at=@next",
            new { userId, next = now.AddSeconds(configuration.CooldownSeconds).UtcDateTime }, transaction);
        transaction.Commit();
        return new(id, extraData);
    }

    public GiftContent? FindBox(uint itemId, int ownerId, uint roomId, uint boxDefinitionId)
    {
        using var connection = database.Connection();
        var rows = connection.Query<ContentRow>("SELECT p.base_id AS BaseId,p.extra_data AS ExtraData FROM user_presents p INNER JOIN items i ON i.id=p.item_id WHERE i.id=@itemId AND i.user_id=@ownerId AND i.room_id=@roomId AND i.base_item=@boxDefinitionId",
            new { itemId, ownerId, roomId, boxDefinitionId }).ToArray();
        return rows.Length == 1 ? new(rows[0].BaseId, rows[0].ExtraData) : null;
    }

    public uint? OpenBox(uint itemId, int ownerId, uint roomId, uint boxDefinitionId, GiftContent content)
    {
        if (roomId == 0) {
            return null;
        }
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.ExecuteScalar<uint?>("SELECT id FROM items WHERE id=@itemId AND user_id=@ownerId AND room_id=@roomId AND base_item=@boxDefinitionId FOR UPDATE",
                new { itemId, ownerId, roomId, boxDefinitionId }, transaction) is null) {
            return null;
        }
        var rows = connection.Query<ContentRow>("SELECT base_id AS BaseId,extra_data AS ExtraData FROM user_presents WHERE item_id=@itemId FOR UPDATE", new { itemId }, transaction).ToArray();
        if (rows.Length != 1 || rows[0].BaseId != content.BaseId || rows[0].ExtraData != content.ExtraData) {
            return null;
        }
        if (connection.Execute("DELETE FROM user_presents WHERE item_id=@itemId", new { itemId }, transaction) != 1
            || connection.Execute("DELETE FROM items WHERE id=@itemId AND user_id=@ownerId AND room_id=@roomId AND base_item=@boxDefinitionId",
                new { itemId, ownerId, roomId, boxDefinitionId }, transaction) != 1) {
            throw new DBConcurrencyException("Recycler box changed during opening.");
        }
        connection.Execute("INSERT INTO items(base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data,limited_number,limited_stack) VALUES(@baseId,@ownerId,0,0,0,0,'',0,@extraData,0,0)",
            new { content.BaseId, ownerId, content.ExtraData }, transaction);
        var rewardId = connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()", transaction: transaction);
        transaction.Commit();
        return rewardId;
    }

    private static RecyclerConfiguration ReadConfiguration(IDbConnection connection, IDbTransaction? transaction = null)
    {
        var suffix = transaction is null ? "" : " LOCK IN SHARE MODE";
        var setting = connection.QuerySingleOrDefault<SettingsRow>("SELECT enabled,slots,cooldown_seconds AS CooldownSeconds FROM recycler_settings WHERE id=1" + suffix, transaction: transaction);
        if (setting is null) {
            return RecyclerConfiguration.Closed;
        }
        var levels = connection.Query<LevelRow>("SELECT level,chance FROM recycler_levels ORDER BY level" + suffix, transaction: transaction).ToArray();
        var prizes = connection.Query<PrizeRow>("SELECT id,level,item_id AS ItemId FROM recycler_prizes ORDER BY level,id" + suffix, transaction: transaction).ToLookup(prize => prize.Level);
        // Orphan prizes also invalidate the dataset instead of silently changing its distribution.
        if (prizes.Any(group => !levels.Any(level => level.Level == group.Key))) {
            return RecyclerConfiguration.Closed;
        }
        return new(setting.Enabled, setting.Slots, setting.CooldownSeconds, levels.Select(level => new RecyclerLevel(level.Level, level.Chance,
            prizes[level.Level].Select(prize => new RecyclerPrize(prize.Id, prize.ItemId)).ToImmutableArray())).ToImmutableArray());
    }

    private sealed class SettingsRow
    {
        public bool Enabled { get; set; }
        public int Slots { get; set; }
        public int CooldownSeconds { get; set; }
    }
    private sealed class LevelRow
    {
        public int Level { get; set; }
        public int Chance { get; set; }
    }
    private sealed class PrizeRow
    {
        public int Id { get; set; }
        public int Level { get; set; }
        public uint ItemId { get; set; }
    }
    private sealed class InputRow
    {
        public uint Id { get; set; }
        public uint ItemId { get; set; }
        public uint LimitedNumber { get; set; }
        public uint LimitedStack { get; set; }
    }
    private sealed class ContentRow
    {
        public uint BaseId { get; set; }
        public string ExtraData { get; set; } = "";
    }
}
