using Dapper;
using Plus.Database;
using Plus.HabboHotel.Users.Clothing.Parts;

namespace Plus.HabboHotel.Users.Clothing;

public interface IClothingStore
{
    IReadOnlyList<ClothingParts> Load(int userId);
    int Add(int userId, int partId, string part);
}

public sealed class ClothingStore(IDatabase database) : IClothingStore
{
    public IReadOnlyList<ClothingParts> Load(int userId)
    {
        using var connection = database.Connection();
        return connection.Query<Row>("SELECT `id`, `part_id` AS PartId, `part` FROM `user_clothing` WHERE `user_id` = @userId", new { userId })
            .Select(row => new ClothingParts(row.Id, row.PartId, row.Part)).ToList();
    }

    public int Add(int userId, int partId, string part)
    {
        using var connection = database.Connection();
        return connection.ExecuteScalar<int>("INSERT INTO `user_clothing` (`user_id`,`part_id`,`part`) VALUES (@userId, @partId, @part); SELECT LAST_INSERT_ID()",
            new { userId, partId, part });
    }

    private sealed record Row(int Id, int PartId, string Part);
}
