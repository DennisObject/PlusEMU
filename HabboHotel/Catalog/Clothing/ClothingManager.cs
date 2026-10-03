using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Clothing;

public class ClothingManager : IClothingManager
{
    private readonly IDatabase _database;
    private Dictionary<int, ClothingItem> _clothing = new();

    public ClothingManager(IDatabase database)
    {
        _database = database;
    }

    public ICollection<ClothingItem> GetClothingAllParts => _clothing.Values;

    // Synchronous and swapped in whole: the catalog reload calls this twice, and an async void reload could
    // fill the same dictionary twice at once and crash the process with an unhandled duplicate key.
    public void Init()
    {
        using var connection = _database.Connection();
        var data = connection.Query<(int Id, string ClothingName, string PartIds)>("SELECT `id`,`clothing_name`,`clothing_parts` FROM `catalog_clothing`");
        var clothing = new Dictionary<int, ClothingItem>();
        foreach (var row in data)
            clothing.Add(row.Id, new(row.Id, row.ClothingName, row.PartIds));
        _clothing = clothing;
    }

    public bool TryGetClothing(int itemId, out ClothingItem clothing) => _clothing.TryGetValue(itemId, out clothing);
}