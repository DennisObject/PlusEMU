using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using Plus.HabboHotel.Users.Clothing.Parts;

namespace Plus.HabboHotel.Users.Clothing;

public sealed class ClothingComponent
{
    /// <summary>
    /// Effects stored by ID > Effect.
    /// </summary>
    private readonly ConcurrentDictionary<int, ClothingParts> _allClothing = new();
    private Habbo _habbo;
    private readonly IClothingStore? _store;

    public ClothingComponent() { }

    internal ClothingComponent(IEnumerable<ClothingParts> clothing, Habbo habbo, IClothingStore store)
    {
        foreach (var part in clothing) _allClothing.TryAdd(part.PartId, part);
        _habbo = habbo;
        _store = store;
    }

    public ICollection<ClothingParts> GetClothingParts => _allClothing.Values;

    /// <summary>
    /// Initializes the EffectsComponent.
    /// </summary>
    /// <param name="UserId"></param>
    public bool Init(Habbo habbo)
    {
        if (_allClothing.Count > 0)
            return false;
        _habbo = habbo;
        return true;
    }

    public void AddClothing(string clothingName, List<int> partIds)
    {
        foreach (var partId in partIds.ToList())
        {
            if (!_allClothing.ContainsKey(partId))
            {
                var newId = (_store ?? throw new InvalidOperationException("Clothing persistence is not configured.")).Add(_habbo.Id, partId, clothingName);
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

    public void PublishCommitted(IEnumerable<ClothingParts> clothing)
    {
        foreach (var part in clothing) _allClothing.TryAdd(part.PartId, part);
    }

}
