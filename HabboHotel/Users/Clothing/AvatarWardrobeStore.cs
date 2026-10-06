using System.Collections.Immutable;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Clothing;

public interface IAvatarWardrobeStore
{
    Task<ImmutableArray<WardrobeSlot>> LoadSlots(int userId);
    void SaveSlot(int userId, int slotId, string look, string gender);
}

public sealed class AvatarWardrobeStore(IDatabase database) : IAvatarWardrobeStore
{
    public async Task<ImmutableArray<WardrobeSlot>> LoadSlots(int userId)
    {
        using var connection = database.Connection();
        var rows = await connection.QueryAsync<SlotRow>("SELECT `slot_id` AS SlotId, `look` AS Look, `gender` AS Gender FROM `user_wardrobe` WHERE `user_id` = @userId", new
        {
            userId
        });

        return rows.Select(row => new WardrobeSlot(row.SlotId, row.Look, row.Gender)).ToImmutableArray();
    }

    public void SaveSlot(int userId, int slotId, string look, string gender)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        // The account row lock serializes saves for one user. user_wardrobe has no unique key, so the existence check is only safe under this lock.
        // A missing account is refused before any slot is read or written, so no orphan wardrobe row can be created.
        var accounts = connection.Query<int>("SELECT `id` FROM `users` WHERE `id` = @userId FOR UPDATE", new
        {
            userId
        }, transaction).ToList();

        if (accounts.Count != 1)
        {
            throw new InvalidOperationException($"Wardrobe save requires exactly one account row for user {userId}.");
        }

        bool exists = connection.Query<int>("SELECT `id` FROM `user_wardrobe` WHERE `user_id` = @userId AND `slot_id` = @slotId FOR UPDATE", new
        {
            userId,
            slotId
        }, transaction).Any();

        if (exists)
        {
            connection.Execute("UPDATE `user_wardrobe` SET `look` = @look, `gender` = @gender WHERE `user_id` = @userId AND `slot_id` = @slotId", new
            {
                userId,
                slotId,
                look,
                gender
            }, transaction);
        }
        else
        {
            connection.Execute("INSERT INTO `user_wardrobe` (`user_id`,`slot_id`,`look`,`gender`) VALUES (@userId,@slotId,@look,@gender)", new
            {
                userId,
                slotId,
                look,
                gender
            }, transaction);
        }

        transaction.Commit();
    }

    private sealed class SlotRow
    {
        public int SlotId
        {
            get; set;
        }
        public string Look { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
    }
}
