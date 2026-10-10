using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>
/// Native editor mappings for the variable, addon and chest families, plus the variable selectors. Core owns bounds,
/// source-set validation, origin binding and persistence; each spec declares footer metadata and derives a plain runtime
/// (the shape its executor validates) from the AIR-form native record. Specs may reject only structural mismatches:
/// an unchosen variable is the valid inactive default, and the box's own validator refuses to save or run it.
/// </summary>
internal static partial class WiredNativeAuxiliaryEditor
{
    internal sealed record Spec(WiredNativeEditorMetadata Metadata, Func<WiredNativeEditorConfiguration, WiredConfiguration?> Compile);

    private static readonly ImmutableArray<int> FurniSources = [0, 100, 101, 200, 201];
    private static readonly ImmutableArray<int> UserSources = [0, 200, 201];

    private static readonly FrozenDictionary<string, Spec> Specs = Variables().Concat(Addons()).Concat(VariableAddons()).Concat(Definitions()).Concat(Chests())
        .ToFrozenDictionary(pair => pair.Key, pair => pair.Value);

    internal static bool Supports(string name) => Specs.ContainsKey(name);

    internal static WiredNativeEditorMetadata Metadata(string name) => Specs.TryGetValue(name, out var spec)
        ? spec.Metadata : throw new InvalidDataException("This native editor projection is unavailable.");

    /// <summary>Return the derived runtime without an origin; core binds the single Native origin afterwards.</summary>
    internal static bool TryCompile(string name, WiredNativeEditorConfiguration native, out WiredConfiguration runtime)
    {
        runtime = new();

        if (!Specs.TryGetValue(name, out var spec) || spec.Compile(native) is not { } compiled) {
            return false;
        }

        runtime = compiled with { Delay = native.Delay ?? 0, Snapshots = native.SavedState.Snapshots };

        return true;
    }

    private static WiredNativeEditorMetadata Meta(int furniSelections, int userSelections, ImmutableArray<int> owned,
        int variables = 0, bool furniPicks = true) => new(
            [.. Enumerable.Range(0, furniSelections).Select(_ => furniPicks ? FurniSources : [0, 200, 201])],
            [.. Enumerable.Range(0, userSelections).Select(_ => UserSources)],
            [.. Enumerable.Repeat(0, furniSelections)], [.. Enumerable.Repeat(0, userSelections)], owned, false)
    {
        VariableDefaults = [.. Enumerable.Repeat(Plus.HabboHotel.Items.Wired.Variables.WiredVariableAbsent.Id, variables)]
    };
}
