using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Plus.Communication.Flash;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Active Octane editors for Plus custom boxes, retaining their original five-column save format.</summary>
public static class WiredLegacyCustomEditor
{
    public static bool IsCustom(IWiredItem box) => box.Type is WiredBoxType.EffectGiveUserBadge
        or WiredBoxType.EffectSetRollerSpeed or WiredBoxType.EffectRegenerateMaps;

    public static IWiredItem? CreateCandidate(IWiredItem original) => original.Type switch
    {
        WiredBoxType.EffectGiveUserBadge => new GiveUserBadgeBox(original.Instance, original.Item),
        WiredBoxType.EffectSetRollerSpeed => new SetRollerSpeedBox(original.Instance, original.Item),
        WiredBoxType.EffectRegenerateMaps => new RegenerateMapsBox(original.Instance, original.Item),
        _ => null
    };

    public static bool TryGetConfiguration(IWiredItem box, out WiredBoxDescriptor descriptor, out WiredConfiguration configuration)
    {
        descriptor = null!;
        configuration = new();
        if (!IsCustom(box))
            return false;
        var code = box.Type switch
        {
            WiredBoxType.EffectGiveUserBadge => 119,
            WiredBoxType.EffectSetRollerSpeed => 88,
            _ => 123 // Generic text/value editor; regenerate has no client-specific layout.
        };
        descriptor = new("plus_legacy_" + box.Type, WiredBoxCategory.Action, code, 0, "Plus custom legacy schema");
        configuration = new()
        {
            Text = box.Type == WiredBoxType.EffectSetRollerSpeed ? string.Empty : box.StringData ?? string.Empty,
            IntParams = box.Type == WiredBoxType.EffectSetRollerSpeed
                ? [int.TryParse(box.StringData, NumberStyles.Integer, CultureInfo.InvariantCulture, out var speed) ? speed : 2]
                : [WiredSources.Trigger, 0, 34]
        };
        return WiredLegacyProtocol.IsWithinLimits(configuration);
    }

    public static bool TryPrepare(IWiredItem original, WiredConfiguration proposed,
        Func<IWiredItem, IWiredItem?> createCandidate, out IWiredItem? candidate, out string error)
    {
        candidate = null;
        error = "Invalid custom Wired settings.";
        if (!IsCustom(original) || !WiredLegacyProtocol.IsWithinLimits(proposed)
            || !proposed.SelectedItems.IsEmpty || !proposed.SecondarySelectedItems.IsEmpty
            || proposed.Delay != 0 || proposed.SelectionCode != 0)
            return false;
        string text;
        if (original.Type == WiredBoxType.EffectSetRollerSpeed)
        {
            if (proposed.IntParams.Length != 1 || proposed.IntParams[0] is < -1 or > 10 || proposed.Text.Length != 0)
                return false;
            text = proposed.IntParams[0].ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            // These legacy executors target the triggering user or room; other user sources have no backing behavior.
            if (!proposed.IntParams.SequenceEqual(new[] { WiredSources.Trigger, 0, 34 }))
                return false;
            if (original.Type == WiredBoxType.EffectGiveUserBadge)
            {
                if (proposed.Text.Length > 50)
                    return false;
                text = proposed.Text;
            }
            else
            {
                if (!string.Equals(proposed.Text, original.StringData ?? string.Empty, StringComparison.Ordinal))
                    return false;
                text = original.StringData ?? string.Empty;
            }
        }
        // Translate the validated active fields into the established legacy text schema, then use detached replay.
        var bytes = Encoding.UTF8.GetBytes(text);
        var buffer = new byte[4 + 2 + bytes.Length + 4 + 4 + 4];
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4, 2), (ushort)bytes.Length);
        bytes.CopyTo(buffer.AsSpan(6));
        return WiredLegacySave.TryPrepare(original, new FlashIncomingPacket { Buffer = buffer }, WiredBoxCategory.Action,
            createCandidate, out candidate, out error);
    }
}
