using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableCatalogTests
{
    [Fact]
    public void DiffChunksChangesAndRemovalsWithoutLosingEmptyFinalAcknowledgment()
    {
        var variables = Enumerable.Range(1, 201).Select(i => new WiredVariableDescription(
            new((uint)i, 1, 5, "v" + i, WiredVariableTarget.User, WiredVariableAvailability.Persistent, true), true, false)).ToArray();
        var catalog = new WiredVariableCatalog(variables);
        var diff = catalog.Diff(new Dictionary<string, int> { ["deleted:1"] = 123 });
        Assert.Equal(new[] { 100, 100, 1 }, diff.Select(x => x.Changed.Count));
        Assert.Equal(new[] { false, false, true }, diff.Select(x => x.LastChunk));
        Assert.Single(diff[0].Removed); Assert.Empty(diff[1].Removed); Assert.Empty(diff[2].Removed);
        var unchanged = Assert.Single(catalog.Diff(variables.ToDictionary(x => x.CatalogId, x => x.Hash)));
        Assert.True(unchanged.LastChunk); Assert.Empty(unchanged.Changed); Assert.Empty(unchanged.Removed);
        Assert.NotEqual(catalog.Hash, new WiredVariableCatalog([variables[0] with { ReadOnly = true }, .. variables.Skip(1)]).Hash);
        Assert.NotEqual(variables[0].Hash, (variables[0] with { TextConnector = new Dictionary<int, string> { [1] = "one" } }).Hash);
    }
    [Fact]
    public void CatalogAndPagesResolveAliasesAndRecheckOwnersOnEveryRequest()
    {
        var directory = new Directory(); var store = new MemoryWiredVariableStore();
        var module = new WiredVariableModule(1, directory, store, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        foreach (var id in Enumerable.Range(1, 5))
            store.Mutate(new(20, WiredVariableTarget.User, id), _ => new(id * 10, DateTimeOffset.FromUnixTimeMilliseconds(1000), DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var catalog = module.DescribeDefinitions([10, 10, 20, 99]);
        var alias = Assert.Single(catalog);
        Assert.Equal("user:10", alias.CatalogId); Assert.Equal(1, alias.CatalogTarget);
        Assert.True(alias.ReadOnly); Assert.True(alias.HasValue);
        var page = module.ReadHolderPage(10, 2, 2, 1);
        Assert.Equal(5, page.Total); Assert.Equal(new[] { 30, 20 }, page.Holders.Select(x => x.Value.Value));
        Assert.All(page.Holders, x => Assert.Equal(10u, x.Key.DefinitionId));
        Assert.Equal(5, module.GetStoredHolders(10).Count);
        var filtered = module.ReadHolderPage(10, 1, 200, 2, [1, 3], new Dictionary<long, string> { [1] = "z", [3] = "a" });
        Assert.Equal(2, filtered.Total); Assert.Equal(new long[] { 3, 1 }, filtered.Holders.Select(x => x.Key.HolderId));
        Assert.Empty(module.ReadHolderPage(10, int.MaxValue, int.MaxValue, 0).Holders);
        Assert.Equal(200, module.ReadHolderPage(10, 0, int.MaxValue, 0).PageSize);
        directory.SourceOwner = 6;
        Assert.Empty(module.DescribeDefinitions([10])); Assert.Empty(module.GetStoredHolders(10));
        Assert.Equal(0, module.ReadHolderPage(10, 1, 20, 0).Total);
    }

    private sealed class Directory : IWiredVariableDirectory
    {
        public uint SourceOwner { get; set; } = 5;
        public uint? GetRoomOwner(uint id) => id == 1 ? 5u : id == 2 ? SourceOwner : null;
        public WiredVariableDefinition? Find(uint id) => id switch
        {
            10 => new(10, 1, 5, "alias", WiredVariableTarget.User, WiredVariableAvailability.RoomActive, true,
                Link: new(2, new(WiredVariableTarget.User, "custom:20"), true)),
            20 => new(20, 2, SourceOwner, "source", WiredVariableTarget.User, WiredVariableAvailability.Shared, true),
            _ => null
        };
    }
}
