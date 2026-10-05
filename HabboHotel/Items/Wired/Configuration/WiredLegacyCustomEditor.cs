using System.Globalization;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Boxes.Triggers;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Active Octane editors for Plus custom boxes, retaining their original five-column save format.</summary>
public static class WiredLegacyCustomEditor
{
    public static bool IsCustom(IWiredItem box) => box.Type is WiredBoxType.EffectGiveUserBadge
        or WiredBoxType.EffectSetRollerSpeed or WiredBoxType.EffectRegenerateMaps or WiredBoxType.TriggerUserSaysCommand;

    public static IWiredItem? CreateCandidate(IWiredItem original) => original.Type switch
    {
        WiredBoxType.EffectGiveUserBadge => ((GiveUserBadgeBox)original).CreateCandidate(),
        WiredBoxType.EffectSetRollerSpeed => new SetRollerSpeedBox(original.Instance, original.Item),
        WiredBoxType.EffectRegenerateMaps => new RegenerateMapsBox(original.Instance, original.Item),
        WiredBoxType.TriggerUserSaysCommand => ((UserSaysCommandBox)original).CreateCandidate(),
        _ => null
    };

    public static bool TryGetConfiguration(IWiredItem box, out WiredBoxDescriptor descriptor, out WiredConfiguration configuration)
    {
        descriptor = null!;
        configuration = new();
        if (!IsCustom(box))
            return false;
        if (box.Type == WiredBoxType.TriggerUserSaysCommand)
        {
            // Same dialog as speech, but command identity and dispatch remain the legacy command runtime.
            descriptor = new("plus_legacy_command", WiredBoxCategory.Trigger, 0, 0, "Plus command trigger schema");
            configuration = new() { IntParams = [0, 1, box.BoolData ? 1 : 0], Text = box.StringData ?? string.Empty };
            return WiredLegacyProtocol.IsWithinLimits(configuration);
        }
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
        if (original.Type == WiredBoxType.TriggerUserSaysCommand)
        {
            if (proposed.IntParams.Length != 3 || proposed.IntParams[0] != 0 || proposed.IntParams[1] != 1
                || proposed.IntParams[2] is < 0 or > 1)
            {
                error = "Command Wired supports the existing command match and hidden feedback settings only.";
                return false;
            }
            var legacy = new WiredConfiguration { IntParams = [proposed.IntParams[2]], Text = proposed.Text };
            return WiredLegacySave.TryPrepare(original, legacy, WiredBoxCategory.Trigger,
                createCandidate, out candidate, out error);
        }
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
        return WiredLegacySave.TryPrepare(original, new WiredConfiguration { Text = text }, WiredBoxCategory.Action,
            createCandidate, out candidate, out error);
    }
}
