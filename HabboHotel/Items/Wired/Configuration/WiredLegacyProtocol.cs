using System.Collections.Immutable;
using System.Text;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Octane's established three-envelope ABI, shared by configured boxes of all six categories.</summary>
public static class WiredLegacyProtocol
{
    // The caller has already read the item id. Never read or mutate the live configuration here.
    public static bool TryRead(IIncomingPacket packet, WiredBoxCategory envelope, out WiredConfiguration configuration)
    {
        configuration = new();
        try
        {
            var intCount = packet.ReadInt();
            if (intCount is < 0 or > WiredConfigurationLimits.IntParams)
                return false;
            var ints = ImmutableArray.CreateBuilder<int>(intCount);
            for (var i = 0; i < intCount; i++)
                ints.Add(packet.ReadInt());
            var text = packet.ReadString();
            var itemCount = packet.ReadInt();
            if (itemCount is < 0 or > WiredConfigurationLimits.SelectedItems)
                return false;
            var items = ImmutableArray.CreateBuilder<uint>(itemCount);
            for (var i = 0; i < itemCount; i++)
                items.Add(packet.ReadUInt());
            configuration = new()
            {
                IntParams = ints.MoveToImmutable(), Text = text, SelectedItems = items.MoveToImmutable(),
                Delay = envelope == WiredBoxCategory.Action ? packet.ReadInt() : 0,
                SelectionCode = packet.ReadInt()
            };
            return !packet.HasDataRemaining() && IsWithinLimits(configuration);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or OverflowException)
        {
            return false;
        }
    }

    public static bool IsWithinLimits(WiredConfiguration configuration) =>
        configuration.Version == WiredConfiguration.CurrentVersion
        && !configuration.IntParams.IsDefault && configuration.IntParams.Length <= WiredConfigurationLimits.IntParams
        && configuration.Text != null && configuration.Text.Length <= WiredConfigurationLimits.TextCharacters
        && Encoding.UTF8.GetByteCount(configuration.Text) <= ushort.MaxValue
        && !configuration.SelectedItems.IsDefault && configuration.SelectedItems.Length <= WiredConfigurationLimits.SelectedItems
        && configuration.SelectedItems.All(id => id > 0 && id <= int.MaxValue)
        && !configuration.SecondarySelectedItems.IsDefault && configuration.SecondarySelectedItems.Length <= WiredConfigurationLimits.SelectedItems
        && configuration.SecondarySelectedItems.All(id => id > 0 && id <= int.MaxValue)
        && configuration.Delay is >= 0 and <= WiredConfigurationLimits.DelayPulses
        && configuration.SelectionCode is >= 0 and <= WiredConfigurationLimits.SelectionCode
        && (configuration.ScoreQuotaPerGame is null or >= 1 and <= 10)
        && !configuration.VariableIds.IsDefault && configuration.VariableIds.Length <= WiredConfigurationLimits.IntParams
        && configuration.VariableIds.All(id => id != null && id.Length <= 1024)
        && !configuration.Snapshots.IsDefault && configuration.Snapshots.Length <= WiredConfigurationLimits.SelectedItems
        && configuration.Snapshots.All(snapshot => snapshot != null && double.IsFinite(snapshot.Z) && snapshot.State != null)
        && configuration.FurniSources != null && configuration.FurniSources.Count <= WiredConfigurationLimits.IntParams
        && configuration.UserSources != null && configuration.UserSources.Count <= WiredConfigurationLimits.IntParams;

    public static void Write(IOutgoingPacket packet, uint itemId, int spriteId, WiredBoxDescriptor descriptor,
        WiredConfiguration configuration, int furniLimit, IReadOnlyList<int> blockedItems)
    {
        packet.WriteBoolean(false);
        packet.WriteInteger(furniLimit);
        packet.WriteInteger(configuration.SelectedItems.Length);
        foreach (var selected in configuration.SelectedItems)
            packet.WriteUInteger(selected);
        packet.WriteInteger(spriteId);
        packet.WriteUInteger(itemId);
        packet.WriteString(configuration.Text);
        packet.WriteInteger(configuration.IntParams.Length);
        foreach (var value in configuration.IntParams)
            packet.WriteInteger(value);
        packet.WriteInteger(configuration.SelectionCode);
        packet.WriteInteger(descriptor.EditorCode);
        if (descriptor.Envelope == WiredBoxCategory.Action)
            packet.WriteInteger(configuration.Delay);
        if (descriptor.Envelope != WiredBoxCategory.Condition)
        {
            packet.WriteInteger(blockedItems.Count);
            foreach (var blocked in blockedItems)
                packet.WriteInteger(blocked);
        }
    }
}
