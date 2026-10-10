using System.Collections.Immutable;
using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Xunit;

namespace Plus.Tests;

public sealed class WiredCurrentSelectorTests
{
    [Fact]
    public void CurrentDefaultsRoundTripThroughTheirNativeCompiler()
    {
        foreach (var descriptor in WiredBoxRegistry.All.Where(d => WiredNativeEditorProjection.Supports(d.CanonicalName))) {
            var native = WiredNativeEditorProjection.DefaultNative(descriptor);
            var reloaded = JsonSerializer.Deserialize<WiredNativeEditorConfiguration>(JsonSerializer.Serialize(native))!;
            Assert.True(WiredNativeEditorProjection.TryCompile(1, descriptor, reloaded, out var runtime), descriptor.CanonicalName);
            Assert.True(WiredNativeEditorProjection.IsBound(1, descriptor, runtime), descriptor.CanonicalName);
        }
    }

    [Fact]
    public void ByTypeUsesItsSelectedSourceAndFilterOutsideTheOwnedFields()
    {
        var world = new WiredSelectorWorld(20, 20, [Furniture(1, 10, 5, 5), Furniture(2, 20, 6, 5), Furniture(3, 20, 7, 5)], []);
        var input = Inputs();
        input.SelectorPool.FurniIds.UnionWith([2u, 3u]);
        input = input with { FurniModified = true };
        var runtime = Compile("wf_slc_furni_bytype", native => native with {
            FurniSourceTypes = [200], OwnedIntParams = [0], Filter = true,
            PrimaryItems = [new(1, false)]
        });
        var result = WiredSelectorModule.SelectRaw("wf_slc_furni_bytype", runtime, world, input);
        Assert.Equal(new uint[] { 2, 3 }, result.Selection.FurniIds.Order().ToArray());
        Assert.True(result.FiltersExisting);
        Assert.Equal(new uint[] { 2, 3 }, WiredSelectorModule.Compose(result, world, input).FurniIds.Order().ToArray());
    }

    [Fact]
    public void NeighborhoodDecodesSpiralBitsAndUsesReachedAvatarSource()
    {
        var world = new WiredSelectorWorld(30, 30, [Furniture(1, 10, 15, 15), Furniture(2, 10, 16, 15), Furniture(3, 10, 16, 14)],
            [new(7, "Reached", WiredSelectorEntityKind.Player, 15, 15)]);
        var runtime = Compile("wf_slc_furni_neighborhood", native => native with {
            OwnedIntParams = [1, 0, 0, 5, .. Enumerable.Repeat(0, 13)], UserSourceTypes = [10]
        });
        var result = WiredSelectorModule.SelectRaw("wf_slc_furni_neighborhood", runtime, world, Inputs() with { ReachedUserId = 7 });
        Assert.Equal(new uint[] { 1, 3 }, result.Selection.FurniIds.Order().ToArray());
        Assert.Empty(WiredSelectorModule.SelectRaw("wf_slc_furni_neighborhood", runtime, world, Inputs()).Selection.FurniIds);
    }

    [Fact]
    public void NeighborhoodSupportsOuterTilesWithoutExpandingRuntimeSettings()
    {
        var fields = new int[17];
        fields[16] = 1 << 24;
        var offsets = WiredSelectorModule.NeighborhoodOffsets(fields.ToImmutableArray()).ToArray();
        Assert.Single(offsets);
        Assert.InRange(offsets[0].X, -10, 10);
        Assert.InRange(offsets[0].Y, -10, 10);
        Assert.True(Math.Abs(offsets[0].X) == 10 || Math.Abs(offsets[0].Y) == 10);
        var runtime = Compile("wf_slc_furni_neighborhood", native => native with { OwnedIntParams = fields.ToImmutableArray() });
        Assert.Equal(17, runtime.IntParams.Length);
    }

    private static WiredConfiguration Compile(string name, Func<WiredNativeEditorConfiguration, WiredNativeEditorConfiguration> edit)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        Assert.True(WiredNativeEditorProjection.TryCompile(1, descriptor, edit(WiredNativeEditorProjection.DefaultNative(descriptor)), out var runtime));
        return runtime;
    }

    private static WiredSelectorInputs Inputs() => new(new(), new(), new());
    private static WiredSelectorFurniture Furniture(uint id, int type, int x, int y) => new(id, type, "Furniture", "0", x, y, 0, 1, [(x, y)]);
}
