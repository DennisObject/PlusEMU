using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;

namespace Plus.HabboHotel.Items.Wired.Configuration;

internal static class WiredNativeSelectorEditor
{
    internal static bool Supports(string name) => WiredSelectorModule.Names.Contains(name, StringComparer.Ordinal)
        && name is not ("wf_slc_furni_with_var" or "wf_slc_users_with_var");

    internal static WiredNativeEditorMetadata Metadata(string name)
    {
        ImmutableArray<int> defaults = name switch {
            "wf_slc_furni_bytype" => [0],
            "wf_slc_users_bytype" => [1],
            "wf_slc_users_team" or "wf_slc_users_handitem" or "wf_slc_users_byaction" or "wf_slc_furni_onfurni" => [0],
            "wf_slc_furni_area" or "wf_slc_users_area" => [0, 0, 0, 0],
            "wf_slc_furni_altitude" => [0, 1],
            "wf_slc_remote" => [0, 0],
            "wf_slc_furni_neighborhood" or "wf_slc_users_neighborhood" => [0, 0, 0, .. Enumerable.Repeat(0, 14)],
            _ => []
        };
        return name switch {
            "wf_slc_furni_bytype" or "wf_slc_furni_onfurni" or "wf_slc_users_onfurni" or "wf_slc_remote" =>
                new([[0, 100, 200, 201]], [], [100], [], defaults, false),
            "wf_slc_furni_neighborhood" or "wf_slc_users_neighborhood" =>
                new([[0, 100, 200, 201]], [[0, 10, 11, 200, 201]], [100], [0], defaults, false),
            _ when Supports(name) => new([], [], [], [], defaults, false),
            _ => throw new InvalidDataException("Unknown native selector.")
        };
    }

    internal static bool TryCompile(string name, WiredNativeEditorConfiguration native, out WiredConfiguration runtime)
    {
        runtime = new();
        if (!Supports(name) || native.Delay is not (null or 0) || native.Quantifier != null || !native.VariableIds.IsEmpty
            || native.Filter == null || native.Inverse == null) return false;
        var p = native.OwnedIntParams;
        var filter = native.Filter.Value ? 1 : 0;
        var invert = native.Inverse.Value ? 1 : 0;
        var sources = ImmutableDictionary<string, int>.Empty;
        var users = ImmutableDictionary<string, int>.Empty;
        var text = native.Text;
        if (text.Length != 0 && name is not ("wf_slc_users_byname" or "wf_slc_users_group" or "wf_slc_users_byaction")) return false;
        ImmutableArray<int> fields;
        switch (name) {
            case "wf_slc_furni_bytype":
                if (p.Length != 1 || p[0] is not (0 or 1)) return false;
                fields = [0, p[0], filter, invert];
                sources = sources.Add("items", native.FurniSourceTypes[0]);
                break;
            case "wf_slc_users_bytype":
                if (p.Length != 1 || p[0] is not (1 or 2 or 4)) return false;
                fields = [p[0], filter, invert];
                break;
            case "wf_slc_users_team":
            case "wf_slc_users_handitem":
                if (p.Length != 1 || p[0] < 0 || name == "wf_slc_users_team" && p[0] > 4) return false;
                fields = [p[0], filter, invert];
                break;
            case "wf_slc_furni_onfurni":
                if (p.Length != 1 || p[0] is < 0 or > 3) return false;
                fields = [p[0], native.FurniSourceTypes[0], filter, invert];
                break;
            case "wf_slc_users_onfurni":
                if (!p.IsEmpty) return false;
                fields = [native.FurniSourceTypes[0], filter, invert];
                break;
            case "wf_slc_users_group":
                if (!p.IsEmpty || text.Length > 0 && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var group) || group <= 0)) return false;
                fields = [text.Length == 0 ? 0 : 1, text.Length == 0 ? 0 : int.Parse(text, CultureInfo.InvariantCulture), filter, invert];
                break;
            case "wf_slc_users_byaction":
                if (p.Length != 1 || !WiredNativeTriggerEditor.TryAvatarAction(p[0], text, out var action)) return false;
                fields = [.. action, filter, invert];
                break;
            case "wf_slc_furni_area":
            case "wf_slc_users_area":
                if (p.Length != 4 || p.Any(value => value < 0)) return false;
                fields = [.. p, filter, invert];
                break;
            case "wf_slc_furni_altitude":
                if (p.Length != 2 || p[0] < 0 || p[1] is < 0 or > 2) return false;
                fields = [p[1], filter, invert];
                text = (p[0] / 100m).ToString(CultureInfo.InvariantCulture);
                break;
            case "wf_slc_furni_neighborhood":
            case "wf_slc_users_neighborhood":
                if (p.Length != 17 || p[0] is not (0 or 1) || p[1] is < -64 or > 64 || p[2] is < -64 or > 64
                    || ((uint)p[16] & 0xfe000000u) != 0) return false;
                fields = p;
                sources = sources.Add("anchor", native.FurniSourceTypes[0]);
                users = users.Add("anchor", native.UserSourceTypes[0]);
                break;
            case "wf_slc_remote":
                if (p.Length != 2 || p[0] is not (0 or 1) || p[1] < 0) return false;
                fields = [filter, invert, p[0], p[1], native.FurniSourceTypes[0]];
                break;
            default:
                if (!p.IsEmpty) return false;
                fields = [filter, invert];
                break;
        }
        runtime = new() {
            IntParams = fields, Text = text,
            SelectedItems = native.PrimaryItems.Select(item => item.ItemId).ToImmutableArray(),
            SecondarySelectedItems = native.SecondaryItems.Select(item => item.ItemId).ToImmutableArray(),
            FurniSources = sources, UserSources = users
        };
        return true;
    }
}
