using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using Plus.HabboHotel.Users.Clothing.Parts;
using Dapper;

namespace Plus.HabboHotel.Users.Clothing;

public sealed class ClothingComponent
{
    /// <summary>
    /// Effects stored by ID > Effect.
    /// </summary>
    private readonly ConcurrentDictionary<int, ClothingParts> _allClothing = new();
    private Habbo _habbo;

    public ICollection<ClothingParts> GetClothingParts => _allClothing.Values;

    /// <summary>
    /// Initializes the EffectsComponent.
    /// </summary>
    /// <param name="UserId"></param>
    public bool Init(Habbo habbo)
    {
        if (_allClothing.Count > 0)
            return false;
        using (var connection = PlusEnvironment.DatabaseManager.Connection())
        {
            foreach (var row in connection.Query<ClothingRow>("SELECT `id`, `part_id` AS PartId, `part` FROM `user_clothing` WHERE `user_id` = @id", new { id = habbo.Id }))
            {
                _allClothing.TryAdd(row.PartId, new(row.Id, row.PartId, row.Part));
            }
        }
        _habbo = habbo;
        return true;
    }

    public void AddClothing(string clothingName, List<int> partIds)
    {
        foreach (var partId in partIds.ToList())
        {
            if (!_allClothing.ContainsKey(partId))
            {
                using var connection = PlusEnvironment.DatabaseManager.Connection();
                var newId = connection.ExecuteScalar<int>("INSERT INTO `user_clothing` (`user_id`,`part_id`,`part`) VALUES (@userId, @partId, @part); SELECT LAST_INSERT_ID()",
                    new { userId = _habbo.Id, partId, part = clothingName });
                _allClothing.TryAdd(partId, new(newId, partId, clothingName));
            }
        }
    }

    public bool TryGet(int partId, [NotNullWhen(true)] out ClothingParts? clothingPart) => _allClothing.TryGetValue(partId, out clothingPart);

    /// <summary>
    /// Disposes the ClothingComponent.
    /// </summary>
    public void Dispose()
    {
        _allClothing.Clear();
    }

    private sealed record ClothingRow(int Id, int PartId, string Part);
}
