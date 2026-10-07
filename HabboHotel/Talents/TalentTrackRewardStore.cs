using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Talents;

public interface ITalentTrackRewardStore
{
    IReadOnlyList<InventoryItem>? Claim(int userId, string type, int level, IReadOnlyCollection<ItemDefinition> gifts);
}

public sealed class TalentTrackRewardStore(IDatabase database) : ITalentTrackRewardStore
{
    public IReadOnlyList<InventoryItem>? Claim(int userId, string type, int level, IReadOnlyCollection<ItemDefinition> gifts)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id=@userId FOR UPDATE", new { userId }, transaction) == null) {
            throw new InvalidOperationException("Talent reward owner no longer exists.");
        }
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_talent_rewards WHERE user_id=@userId AND type=@type AND level=@level",
            new { userId, type, level }, transaction) != 0) {
            return null;
        }
        connection.Execute("INSERT INTO user_talent_rewards(user_id,type,level) VALUES(@userId,@type,@level)", new { userId, type, level }, transaction);
        var awarded = new List<InventoryItem>();
        foreach (var gift in gifts) {
            var id = connection.ExecuteScalar<uint>("INSERT INTO items(user_id,base_item,extra_data) VALUES(@userId,@baseId,''); SELECT LAST_INSERT_ID()",
                new { userId, baseId = gift.Id }, transaction);
            awarded.Add(new() { Id = id, OwnerId = checked((uint)userId), Definition = gift, ExtraData = FurniExtraData.Load(gift, "", keepLegacy: true) });
        }
        transaction.Commit();
        return awarded;
    }
}
