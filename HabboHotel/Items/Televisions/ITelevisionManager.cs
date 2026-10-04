using System.Diagnostics.CodeAnalysis;

namespace Plus.HabboHotel.Items.Televisions;

public interface ITelevisionManager
{
    ICollection<TelevisionItem> TelevisionList { get; }
    Dictionary<int, TelevisionItem> Televisions { get; }
    void Init();
    bool TryGet(int itemId, [NotNullWhen(true)] out TelevisionItem? televisionItem);
}