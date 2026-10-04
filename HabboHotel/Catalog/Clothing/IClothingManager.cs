using System.Diagnostics.CodeAnalysis;
namespace Plus.HabboHotel.Catalog.Clothing;

public interface IClothingManager
{
    ICollection<ClothingItem> GetClothingAllParts { get; }
    void Init();
    bool TryGetClothing(int itemId, [NotNullWhen(true)] out ClothingItem? clothing);
}