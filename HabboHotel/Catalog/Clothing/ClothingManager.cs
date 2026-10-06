using Plus.Core;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Catalog.Clothing;

public class ClothingManager : IClothingManager, IStartable
{
    private readonly IDatabase _database;
    private Dictionary<int, ClothingItem> _clothing = new();

    public ClothingManager(IDatabase database)
    {
        _database = database;
    }

    public ICollection<ClothingItem> GetClothingAllParts => _clothing.Values;

    // Publish a complete replacement so simultaneous reloads cannot populate the same dictionary.
    public int StartOrder => 20;
    public Task Start() => Load();
    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var data = await connection.QueryAsync<(int Id, string ClothingName, string PartIds)>("SELECT id, clothing_name, clothing_parts FROM catalog_clothing");
        _clothing = data.ToDictionary(row => row.Id, row => new ClothingItem(row.Id, row.ClothingName, row.PartIds));
    }

    public bool TryGetClothing(int itemId, [NotNullWhen(true)] out ClothingItem? clothing) => _clothing.TryGetValue(itemId, out clothing);
}
