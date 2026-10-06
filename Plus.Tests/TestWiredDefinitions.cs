using Plus.HabboHotel.Items;

namespace Plus.Tests;

internal sealed class TestWiredDefinitions(Func<Dictionary<uint, ItemDefinition>> lookup) : IItemDataManager
{
    public static TestWiredDefinitions Unused
    {
        get;
    } = new(() =>
        throw new InvalidOperationException("Unexpected furniture definition lookup."));

    public Dictionary<uint, ItemDefinition> Items => lookup();
    public Dictionary<int, uint> Gifts => throw new NotSupportedException();
    public ItemDefinition? GetItemByName(string name) => throw new NotSupportedException();
    public void Init() => throw new NotSupportedException();
}
