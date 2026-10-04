using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Effects;

public interface IAvatarEffectStore
{
    IReadOnlyList<AvatarEffect> Load(int userId);
    AvatarEffect Create(int userId, int spriteId, double duration);
    void Activate(int id, DateTimeOffset timestamp);
    void SaveQuantity(int id, int quantity, bool activated, DateTimeOffset? activatedAt);
}

public sealed class AvatarEffectStore(IDatabase database) : IAvatarEffectStore
{
    public IReadOnlyList<AvatarEffect> Load(int userId)
    {
        using var connection = database.Connection();
        var rows = connection.Query<EffectRow>("SELECT `id`, `user_id` AS UserId, `effect_id` AS SpriteId, `total_duration` AS Duration, " +
            "`is_activated` AS Activated, `activated_stamp` AS TimestampActivated, `quantity` FROM `user_effects` WHERE `user_id` = @userId", new { userId });
        return rows.Select(row => new AvatarEffect(row.Id, row.UserId, row.SpriteId, row.Duration, row.Activated,
            row.TimestampActivated, row.Quantity, this)).ToList();
    }

    public AvatarEffect Create(int userId, int spriteId, double duration)
    {
        using var connection = database.Connection();
        var id = connection.ExecuteScalar<int>("INSERT INTO `user_effects` (`user_id`,`effect_id`,`total_duration`,`is_activated`,`activated_stamp`,`quantity`) " +
            "VALUES (@userId, @spriteId, @duration, false, NULL, 1); SELECT LAST_INSERT_ID()", new { userId, spriteId, duration });
        return new(id, userId, spriteId, duration, false, null, 1, this);
    }

    public void Activate(int id, DateTimeOffset timestamp)
    {
        using var connection = database.Connection();
        connection.Execute("UPDATE `user_effects` SET `is_activated` = true, `activated_stamp` = @timestamp WHERE `id` = @id", new { timestamp, id });
    }

    public void SaveQuantity(int id, int quantity, bool activated, DateTimeOffset? activatedAt)
    {
        using var connection = database.Connection();
        if (quantity < 1) connection.Execute("DELETE FROM `user_effects` WHERE `id` = @id", new { id });
        else connection.Execute("UPDATE `user_effects` SET `quantity` = @quantity, `is_activated` = @activated, `activated_stamp` = @activatedStamp WHERE `id` = @id",
            new { quantity, activated, activatedStamp = activatedAt?.UtcDateTime, id });
    }

    private sealed record EffectRow(int Id, int UserId, int SpriteId, double Duration, bool Activated, DateTimeOffset? TimestampActivated, int Quantity);
}
