using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;

namespace Plus.HabboHotel.Items.Wired.Configuration;

internal static class WiredNativeConditionEditor
{
    private static string Positive(string name) => WiredConditionConfiguration.NegativeNames.GetValueOrDefault(name, name);
    internal static bool Supports(string name) => WiredConditionConfiguration.Supports(name)
        && name is not ("wf_cnd_furnis_hv_avtrs" or "wf_cnd_user_count_in");

    internal static WiredNativeEditorMetadata Metadata(string name)
    {
        var positive = Positive(name);
        ImmutableArray<int> defaults = positive switch {
            "wf_cnd_actor_dir" => [255],
            "wf_cnd_actor_in_team" or "wf_cnd_has_handitem" or "wf_cnd_wearing_effect" => [0],
            "wf_cnd_user_performs_action" => [0],
            "wf_cnd_triggerer_match" => [1],
            "wf_cnd_furnis_hv_avtrs" or "wf_cnd_has_furni_on" => [1],
            "wf_cnd_match_snapshot" => [0, 0, 0, 0],
            "wf_cnd_has_altitude" => [0, 1],
            "wf_cnd_slc_quantity" => [0, 0, 1],
            "wf_cnd_user_count_in" => [1, 50],
            "wf_cnd_team_has_rank" => [1, 0],
            "wf_cnd_team_has_score" => [1, 0, 1],
            "wf_cnd_counter_time_matches" => [0, 0, 0, 1],
            "wf_cnd_time_less_than" or "wf_cnd_time_more_than" => [1],
            "wf_cnd_date_rng_active" => [0, 0],
            "wf_cnd_match_time" => [0, 0, 0, 0, 59, 0, 59, 0, 23],
            "wf_cnd_match_date" => [0, 0, 127, 1, 31, 4095, DateTime.UtcNow.Year, DateTime.UtcNow.Year],
            _ => []
        };
        WiredNativeEditorMetadata metadata = positive switch {
            "wf_cnd_trggrer_on_frn" => new([[0, 100, 200, 201]], [[0, 10, 11, 200, 201]], [100], [0], defaults, false),
            "wf_cnd_triggerer_match" => new([], [[0, 10, 11, 200, 201], [0, 10, 11, 101, 200, 201]], [], [0, 0], defaults, false),
            "wf_cnd_stuff_is" => new([[0, 100, 101, 200, 201], [0, 100, 101, 200, 201]], [], [100, 101], [], defaults, true),
            "wf_cnd_slc_quantity" => new([[0, 100, 200, 201]], [[0, 10, 11, 200, 201]], [100], [0], defaults, true),
            "wf_cnd_furnis_hv_avtrs" or "wf_cnd_has_furni_on" or "wf_cnd_match_snapshot"
                or "wf_cnd_has_altitude" or "wf_cnd_valid_moves" or "wf_cnd_counter_time_matches" =>
                new([[0, 100, 200, 201]], [], [100], [], defaults, positive == "wf_cnd_has_altitude"),
            "wf_cnd_actor_dir" or "wf_cnd_actor_in_group" or "wf_cnd_actor_in_team" or "wf_cnd_has_handitem"
                or "wf_cnd_wearing_effect" or "wf_cnd_wearing_badge" or "wf_cnd_user_performs_action"
                or "wf_cnd_team_has_rank" or "wf_cnd_team_has_score" =>
                new([], [[0, 10, 11, 200, 201]], [], [0], defaults, false),
            _ when Supports(name) => new([], [], [], [], defaults, false),
            _ => throw new InvalidDataException("Unknown native condition.")
        };
        var quantifier = positive switch {
            "wf_cnd_match_snapshot" or "wf_cnd_stuff_is" or "wf_cnd_has_altitude" or "wf_cnd_counter_time_matches" => 1,
            "wf_cnd_actor_dir" or "wf_cnd_actor_in_group" or "wf_cnd_actor_in_team" or "wf_cnd_has_handitem"
                or "wf_cnd_wearing_effect" or "wf_cnd_wearing_badge" or "wf_cnd_user_performs_action"
                or "wf_cnd_triggerer_match" or "wf_cnd_trggrer_on_frn" or "wf_cnd_team_has_rank"
                or "wf_cnd_team_has_score" => 2,
            _ => 0
        };
        return metadata with { QuantifierType = quantifier, Invert = name != positive };
    }

    internal static bool TryCompile(string name, WiredNativeEditorConfiguration native, out WiredConfiguration runtime)
    {
        runtime = new();
        if (!Supports(name) || native.Delay != null || native.Quantifier is not (0 or 1) || !native.VariableIds.IsEmpty) return false;
        var p = native.OwnedIntParams;
        var f = native.FurniSourceTypes;
        var u = native.UserSourceTypes;
        var q = native.Quantifier.Value;
        var positive = Positive(name);
        if (Metadata(name).QuantifierType == 0 && q != 0) return false;
        var text = native.Text;
        ImmutableArray<int> parameters;
        switch (positive) {
            case "wf_cnd_actor_dir":
            case "wf_cnd_actor_in_team":
            case "wf_cnd_has_handitem":
            case "wf_cnd_wearing_effect":
                if (p.Length != 1 || u.Length != 1) return false;
                parameters = [p[0], u[0], q];
                break;
            case "wf_cnd_actor_in_group":
                if (!p.IsEmpty || u.Length != 1) return false;
                var groupId = 0;
                if (text.Length != 0 && (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out groupId) || groupId <= 0)) return false;
                parameters = [u[0], text.Length == 0 ? 0 : 1, groupId, q];
                break;
            case "wf_cnd_wearing_badge":
                if (!p.IsEmpty || u.Length != 1) return false;
                parameters = [u[0], q];
                break;
            case "wf_cnd_user_performs_action":
                if (p.Length != 1 || u.Length != 1 || !WiredNativeTriggerEditor.TryAvatarAction(p[0], text, out var action)) return false;
                parameters = [.. action, u[0], q];
                break;
            case "wf_cnd_triggerer_match":
                if (p.Length != 1 || u.Length != 2) return false;
                parameters = [p[0], text.Length == 0 ? 0 : 1, u[0], u[1], q];
                break;
            case "wf_cnd_trggrer_on_frn":
                if (!p.IsEmpty || f.Length != 1 || u.Length != 1) return false;
                parameters = [f[0], u[0], q];
                break;
            case "wf_cnd_furnis_hv_avtrs":
            case "wf_cnd_has_furni_on":
                if (p.Length != 1 || f.Length != 1 || q != 0) return false;
                parameters = [p[0], f[0]];
                break;
            case "wf_cnd_match_snapshot":
                if (p.Length != 4 || f.Length != 1) return false;
                parameters = [.. p, f[0], q];
                break;
            case "wf_cnd_stuff_is":
                if (!p.IsEmpty || f.Length != 2) return false;
                // Source 101 addresses the second independently counted furniture selection.
                parameters = [f[0] == 101 ? 100 : f[0], f[1] == 101 ? 100 : f[1], q];
                text = string.Join(';', native.SecondaryItems.Select(item => item.ItemId));
                break;
            case "wf_cnd_has_altitude":
                if (p.Length != 2 || f.Length != 1 || p[0] is < 0 or > 8000) return false;
                parameters = [p[1], f[0], q];
                text = (p[0] / 100m).ToString(CultureInfo.InvariantCulture);
                break;
            case "wf_cnd_valid_moves":
                if (!p.IsEmpty || f.Length != 1) return false;
                parameters = [];
                break;
            case "wf_cnd_slc_quantity":
                if (p.Length != 3 || f.Length != 1 || u.Length != 1 || p[0] is not (0 or 1) || p[1] is < 0 or > 100) return false;
                parameters = [p[2], p[1], p[0] == 0 ? 1 : 0, p[0] == 0 ? f[0] : u[0]];
                break;
            case "wf_cnd_user_count_in":
                if (p.Length != 2 || p.Any(value => value is < 0 or > 125) || q != 0) return false;
                parameters = [p[0], p[1], 0];
                break;
            case "wf_cnd_team_has_rank":
                if (p.Length != 2 || u.Length != 1) return false;
                parameters = [p[0], p[1], u[0], q];
                break;
            case "wf_cnd_team_has_score":
                if (p.Length != 3 || u.Length != 1) return false;
                parameters = [p[0], p[2], p[1], u[0], q];
                break;
            case "wf_cnd_counter_time_matches":
                if (p.Length != 4 || f.Length != 1 || p[0] is < 0 or > 59 || p[2] is not (0 or 1)) return false;
                parameters = [p[3], p[1], p[0] * 2 + p[2], f[0], q];
                break;
            case "wf_cnd_time_less_than":
            case "wf_cnd_time_more_than":
                if (p.Length != 1 || p[0] is < 1 or > 1200) return false;
                parameters = p;
                break;
            case "wf_cnd_date_rng_active":
                if (p.Length is not (1 or 2)) return false;
                parameters = [p[0], p.Length == 2 ? p[1] : 0];
                break;
            case "wf_cnd_match_time":
                if (p.Length != 9 || p.Take(3).Any(value => value is not (0 or 1))) return false;
                parameters = [p[2] * 2, p[7], p[8], p[1] * 2, p[5], p[6], p[0] * 2, p[3], p[4]];
                break;
            case "wf_cnd_match_date":
                if (p.Length != 8 || p.Take(2).Any(value => value is not (0 or 1))) return false;
                parameters = [p[2], p[0] * 2, p[3], p[4], p[5], p[1] * 2, p[6], p[7]];
                break;
            default:
                return false;
        }
        if (positive is not ("wf_cnd_actor_in_group" or "wf_cnd_wearing_badge" or "wf_cnd_triggerer_match"
            or "wf_cnd_user_performs_action") && native.Text.Length != 0) return false;

        var proposed = new WiredConfiguration {
            IntParams = parameters, Text = text,
            SelectedItems = native.PrimaryItems.Select(item => item.ItemId).ToImmutableArray(),
            SecondarySelectedItems = native.SecondaryItems.Select(item => item.ItemId).ToImmutableArray(),
            Snapshots = native.SavedState.Snapshots
        };
        if (!WiredConditionConfiguration.TryValidate(name, proposed, out runtime, out _)) return false;
        if (positive == "wf_cnd_stuff_is") {
            runtime = runtime with { FurniSources = runtime.FurniSources.SetItem("items", f[0]).SetItem("comparison", f[1]) };
        }
        else if (positive == "wf_cnd_valid_moves") {
            runtime = runtime with { FurniSources = runtime.FurniSources.SetItem("items", f[0]) };
        }
        return true;
    }
}
