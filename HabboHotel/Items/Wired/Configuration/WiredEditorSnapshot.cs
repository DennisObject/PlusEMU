using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Modern.Actions;

namespace Plus.HabboHotel.Items.Wired.Configuration;

public sealed record WiredEditorSnapshot(
    uint ItemId, int SpriteId, WiredBoxDescriptor Descriptor, WiredConfiguration Configuration,
    int FurniLimit, ImmutableArray<int> BlockedItems)
{
    public WiredNativeEditorConfiguration? Native { get; init; }

    /// <summary>The room variable catalog hash for forms with variable pickers; null for forms without any.</summary>
    public int? CatalogHash { get; init; }

    /// <summary>The shared variables a reference box may point at; null for every other form.</summary>
    public ImmutableArray<Plus.HabboHotel.Items.Wired.Variables.WiredNativeSharedVariable>? SharedVariables { get; init; }

    public static WiredEditorSnapshot Capture(IWiredConfiguredItem box)
    {
        var configuration = box.Configuration;

        if (!WiredNativeEditorProjection.TryProject(box.Item, box.Descriptor, configuration, out var native)) {
            throw new InvalidDataException("The saved settings have no supported native editor projection.");
        }

        // Pickers need the catalog hash; a failed capture sends the zero request hint, never a successful empty catalog.
        int? hash = WiredNativeEditorProjection.Metadata(box.Descriptor.CanonicalName).VariableDefaults.Length == 0 ? null
            : box.Instance.GetWired().Variables.TryCaptureNativeCatalog(out var catalog) && catalog is not null ? catalog.Hash : 0;

        return new(box.Item.Id, box.Item.Definition.SpriteId, box.Descriptor, configuration,
            WiredConfigurationLimits.SelectedItems, [])
        {
            Native = native,
            CatalogHash = hash,
            SharedVariables = box.Descriptor.CanonicalName == "wf_var_reference"
                ? [.. box.Instance.GetWired().Variables.Module.ListShared().Select(Plus.HabboHotel.Items.Wired.Variables.WiredNativeSharedVariable.Project)] : null
        };
    }
}
