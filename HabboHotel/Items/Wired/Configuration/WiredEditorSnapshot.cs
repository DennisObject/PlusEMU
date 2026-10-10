using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Modern.Actions;

namespace Plus.HabboHotel.Items.Wired.Configuration;

public sealed record WiredEditorSnapshot(
    uint ItemId, int SpriteId, WiredBoxDescriptor Descriptor, WiredConfiguration Configuration,
    int FurniLimit, ImmutableArray<int> BlockedItems)
{
    public WiredNativeEditorConfiguration? Native { get; init; }

    public static WiredEditorSnapshot Capture(IWiredConfiguredItem box)
    {
        var configuration = box.Configuration;

        if (!WiredNativeEditorProjection.TryProject(box.Item, box.Descriptor, configuration, out var native)) {
            throw new InvalidDataException("The saved settings have no supported native editor projection.");
        }

        return new(box.Item.Id, box.Item.Definition.SpriteId, box.Descriptor, configuration,
            WiredConfigurationLimits.SelectedItems, [])
        { Native = native };
    }

    public static WiredEditorSnapshot Capture(Item item, WiredBoxDescriptor descriptor, WiredConfiguration configuration,
        int furniLimit = WiredConfigurationLimits.SelectedItems, IReadOnlyList<int>? blockedItems = null) =>
        new(item.Id, item.Definition.SpriteId, descriptor,
            WiredMovementConfiguration.ForEditor(descriptor.CanonicalName, configuration), furniLimit,
            blockedItems?.ToImmutableArray() ?? []);
}
