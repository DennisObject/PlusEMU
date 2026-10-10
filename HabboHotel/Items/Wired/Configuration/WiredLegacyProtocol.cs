using System.Collections.Immutable;
using System.Text;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Octane's established three-envelope ABI, shared by configured boxes of all six categories.</summary>
public static class WiredLegacyProtocol
{
    // The caller already read the owning floor item ID. Exactly one canonical body is accepted.
    public static bool TryRead(IIncomingPacket packet, WiredBoxCategory category, out WiredConfiguration configuration)
    {
        var accepted = TryReadNative(packet, category, out var native);
        configuration = accepted ? WireDraft(native) : new();

        return accepted;
    }

    internal static WiredConfiguration WireDraft(WiredNativeEditorConfiguration native) => new()
    {
        IntParams = native.OwnedIntParams,
        Text = native.Text,
        SelectedItems = native.PrimaryItems.Select(item => item.ItemId).ToImmutableArray(),
        SecondarySelectedItems = native.SecondaryItems.Select(item => item.ItemId).ToImmutableArray(),
        VariableIds = native.VariableIds,
        Delay = native.Delay ?? 0
    };

    public static bool TryReadNative(IIncomingPacket packet, WiredBoxCategory category, out WiredNativeEditorConfiguration native)
    {
        native = new();

        try {
            var owned = ReadInts(packet, WiredConfigurationLimits.IntParams);
            var text = packet.ReadString();
            var primary = ReadItems(packet);
            int? delay = category == WiredBoxCategory.Action ? packet.ReadInt() : null;
            int? quantifier = category == WiredBoxCategory.Condition ? packet.ReadInt() : null;
            bool? filter = category == WiredBoxCategory.Selector ? ReadBoolean(packet) : null;
            bool? inverse = category == WiredBoxCategory.Selector ? ReadBoolean(packet) : null;
            var furni = ReadInts(packet, WiredConfigurationLimits.IntParams);
            var users = ReadInts(packet, WiredConfigurationLimits.IntParams);
            var variableCount = ReadCount(packet, WiredConfigurationLimits.IntParams);
            var variables = ImmutableArray.CreateBuilder<string>(variableCount);

            for (var index = 0; index < variableCount; index++) {
                variables.Add(packet.ReadString());
            }

            var secondary = ReadItems(packet);
            native = new()
            {
                Category = category,
                OwnedIntParams = owned,
                Text = text,
                PrimaryItems = primary,
                SecondaryItems = secondary,
                FurniSourceTypes = furni,
                UserSourceTypes = users,
                VariableIds = variables.MoveToImmutable(),
                Delay = delay,
                Quantifier = quantifier,
                Filter = filter,
                Inverse = inverse
            };

            return !packet.HasDataRemaining() && WiredNativeEditorProjection.WithinBounds(native);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or OverflowException) {
            return false;
        }
    }

    private static bool ReadBoolean(IIncomingPacket packet) => packet.ReadByte() switch
    {
        0 => false,
        1 => true,
        _ => throw new InvalidDataException("Invalid native Wired boolean.")
    };

    private static int ReadCount(IIncomingPacket packet, int maximum)
    {
        var count = packet.ReadInt();

        return count is >= 0 && count <= maximum ? count : throw new InvalidDataException("Invalid native Wired count.");
    }

    private static ImmutableArray<int> ReadInts(IIncomingPacket packet, int maximum)
    {
        var count = ReadCount(packet, maximum);
        var values = ImmutableArray.CreateBuilder<int>(count);

        for (var index = 0; index < count; index++) {
            values.Add(packet.ReadInt());
        }

        return values.MoveToImmutable();
    }

    private static ImmutableArray<WiredNativeItemReference> ReadItems(IIncomingPacket packet)
    {
        var count = ReadCount(packet, WiredConfigurationLimits.SelectedItems);
        var items = ImmutableArray.CreateBuilder<WiredNativeItemReference>(count);

        for (var index = 0; index < count; index++) {
            var id = packet.ReadInt();

            if (id is 0 or int.MinValue) {
                throw new InvalidDataException("Invalid native furniture identity.");
            }

            items.Add(new((uint)Math.Abs(id), id < 0));
        }

        return items.MoveToImmutable();
    }

    public static bool IsWithinLimits(WiredConfiguration configuration) =>
        configuration.Version == WiredConfiguration.CurrentVersion
        && !configuration.IntParams.IsDefault && configuration.IntParams.Length <= WiredConfigurationLimits.IntParams
        && configuration.Text != null && configuration.Text.Length <= WiredConfigurationLimits.TextCharacters
        && Encoding.UTF8.GetByteCount(configuration.Text) <= ushort.MaxValue
        && !configuration.SelectedItems.IsDefault && configuration.SelectedItems.Length <= WiredConfigurationLimits.SelectedItems
        && configuration.SelectedItems.All(id => id > 0)
        && !configuration.SecondarySelectedItems.IsDefault && configuration.SecondarySelectedItems.Length <= WiredConfigurationLimits.SelectedItems
        && configuration.SecondarySelectedItems.All(id => id > 0)
        && configuration.Delay is >= 0 and <= WiredConfigurationLimits.DelayPulses
        && configuration.SelectionCode is >= 0 and <= WiredConfigurationLimits.SelectionCode
        && (configuration.ScoreQuotaPerGame is null or >= 1 and <= 10)
        && (configuration.TemporaryPlacement?.IsWithinLimits() ?? true)
        && !configuration.VariableIds.IsDefault && configuration.VariableIds.Length <= WiredConfigurationLimits.IntParams
        && configuration.VariableIds.All(id => id != null && id.Length <= 1024)
        && !configuration.Snapshots.IsDefault && configuration.Snapshots.Length <= WiredConfigurationLimits.SelectedItems
        && configuration.Snapshots.All(snapshot => snapshot != null && double.IsFinite(snapshot.Z) && snapshot.State != null
            && (snapshot.Wall?.IsWithinLimits() ?? true))
        && configuration.FurniSources != null && configuration.FurniSources.Count <= WiredConfigurationLimits.IntParams
        && configuration.UserSources != null && configuration.UserSources.Count <= WiredConfigurationLimits.IntParams;

    public static void Write(IOutgoingPacket packet, uint itemId, int spriteId, WiredBoxDescriptor descriptor,
        WiredConfiguration configuration, int furniLimit, IReadOnlyList<int> blockedItems,
        WiredNativeEditorConfiguration? editor = null)
    {
        editor ??= configuration.Origin?.Native;

        if (editor == null || !WiredNativeEditorProjection.Supports(descriptor.CanonicalName)
            || editor.NativeCode != WiredNativeEditorProjection.Code(descriptor.CanonicalName)) {
            throw new InvalidDataException("This box has no proven canonical native editor projection.");
        }

        var metadata = WiredNativeEditorProjection.Metadata(descriptor.CanonicalName);
        packet.WriteInteger(furniLimit);
        WriteInts(packet, editor.PrimaryItems.Select(item => item.WireId));
        WriteInts(packet, editor.SecondaryItems.Select(item => item.WireId));
        packet.WriteInteger(spriteId);
        packet.WriteUInteger(itemId);
        packet.WriteString(editor.Text);
        WriteInts(packet, editor.OwnedIntParams);
        packet.WriteInteger(editor.VariableIds.Length);

        foreach (var token in editor.VariableIds) {
            packet.WriteString(token);
        }

        WriteInts(packet, editor.FurniSourceTypes);
        WriteInts(packet, editor.UserSourceTypes);
        packet.WriteInteger(editor.NativeCode);

        if (editor.Category == WiredBoxCategory.Action) {
            packet.WriteInteger(editor.Delay!.Value);
        }

        packet.WriteBoolean(true);
        WriteGroups(packet, metadata.FurniAllowed);
        WriteGroups(packet, metadata.UsersAllowed);
        WriteInts(packet, metadata.FurniDefaults);
        WriteInts(packet, metadata.UserDefaults);
        packet.WriteBoolean(metadata.AllowWall);
        // These six action cards have no variable/context inputs. Unknown card contexts are not fabricated.
        packet.WriteInteger(0);
        WriteInts(packet, metadata.OwnedDefaults);
    }

    private static void WriteGroups(IOutgoingPacket packet, ImmutableArray<ImmutableArray<int>> groups)
    {
        packet.WriteInteger(groups.Length);

        foreach (var group in groups) {
            WriteInts(packet, group);
        }
    }

    private static void WriteInts(IOutgoingPacket packet, IEnumerable<int> values)
    {
        var array = values.ToArray();
        packet.WriteInteger(array.Length);

        foreach (var value in array) {
            packet.WriteInteger(value);
        }
    }
}
