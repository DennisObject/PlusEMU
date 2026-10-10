using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Conditions;

public static class WiredConditionConfiguration
{
    public static readonly IReadOnlyDictionary<string, string> NegativeNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["wf_cnd_not_triggerer_match"] = "wf_cnd_triggerer_match",
        ["wf_cnd_not_trggrer_on"] = "wf_cnd_trggrer_on_frn",
        ["wf_cnd_not_in_group"] = "wf_cnd_actor_in_group",
        ["wf_cnd_not_wearing_fx"] = "wf_cnd_wearing_effect",
        ["wf_cnd_not_has_handitem"] = "wf_cnd_has_handitem",
        ["wf_cnd_not_wearing_b"] = "wf_cnd_wearing_badge",
        ["wf_cnd_not_in_team"] = "wf_cnd_actor_in_team",
        ["wf_cnd_not_user_performs_action"] = "wf_cnd_user_performs_action",
        ["wf_cnd_not_hv_avtrs"] = "wf_cnd_furnis_hv_avtrs",
        ["wf_cnd_not_furni_on"] = "wf_cnd_has_furni_on",
        ["wf_cnd_not_match_snap"] = "wf_cnd_match_snapshot",
        ["wf_cnd_not_stuff_is"] = "wf_cnd_stuff_is",
        ["wf_cnd_not_user_count"] = "wf_cnd_user_count_in"
    };
    public static readonly IReadOnlySet<string> PositiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "wf_cnd_actor_dir", "wf_cnd_actor_in_group", "wf_cnd_actor_in_team", "wf_cnd_counter_time_matches",
        "wf_cnd_date_rng_active", "wf_cnd_furnis_hv_avtrs", "wf_cnd_has_altitude", "wf_cnd_has_furni_on",
        "wf_cnd_has_handitem", "wf_cnd_match_date", "wf_cnd_match_snapshot", "wf_cnd_match_time",
        "wf_cnd_slc_quantity", "wf_cnd_stuff_is", "wf_cnd_team_has_rank", "wf_cnd_team_has_score",
        "wf_cnd_time_less_than", "wf_cnd_time_more_than", "wf_cnd_trggrer_on_frn", "wf_cnd_triggerer_match",
        "wf_cnd_user_count_in", "wf_cnd_user_performs_action", "wf_cnd_valid_moves", "wf_cnd_wearing_badge", "wf_cnd_wearing_effect"
    };
    public static bool Supports(string name) => PositiveNames.Contains(name) || NegativeNames.ContainsKey(name);

    public static WiredConfiguration Defaults(string name, int calendarYear)
    {
        var positive = NegativeNames.GetValueOrDefault(name, name);
        ImmutableArray<int> parameters = positive switch
        {
            "wf_cnd_actor_dir" => [255, 0, 0],
            "wf_cnd_actor_in_group" => [0, 0, 0, 0],
            "wf_cnd_actor_in_team" or "wf_cnd_has_handitem" or "wf_cnd_wearing_effect" => [0, 0, 1],
            "wf_cnd_wearing_badge" => [0, 0],
            "wf_cnd_user_performs_action" => [1, 0, 0, 0, 1, 0, 0],
            "wf_cnd_triggerer_match" => [1, 0, 0, 0, 0],
            "wf_cnd_trggrer_on_frn" => [100, 0, 0],
            "wf_cnd_furnis_hv_avtrs" or "wf_cnd_has_furni_on" => [0, 100],
            "wf_cnd_match_snapshot" => [0, 0, 0, 0, 100, 0],
            "wf_cnd_stuff_is" => [0, 0, 0],
            "wf_cnd_has_altitude" => [1, 100, 0],
            "wf_cnd_valid_moves" => [],
            "wf_cnd_slc_quantity" => [1, 0, 0, 0],
            "wf_cnd_user_count_in" => [1, 125, 0],
            "wf_cnd_team_has_rank" => [1, 1, 0, 0],
            "wf_cnd_team_has_score" => [1, 1, 0, 0, 0],
            "wf_cnd_counter_time_matches" => [1, 0, 0, 100, 0],
            "wf_cnd_time_less_than" or "wf_cnd_time_more_than" => [0],
            "wf_cnd_date_rng_active" => [0, 0],
            "wf_cnd_match_time" => [0, 0, 0, 0, 0, 0, 0, 0, 0],
            "wf_cnd_match_date" => [127, 0, 1, 31, 4095, 0, calendarYear, calendarYear],
            _ => throw new ArgumentException("Unknown condition.", nameof(name))
        };

        if (!TryValidate(name, new() { IntParams = parameters, Text = positive == "wf_cnd_has_altitude" ? "0" : "" }, out var config, out var error)) {
            throw new InvalidOperationException(error);
        }

        return config;
    }

    public static bool TryValidate(string name, WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Invalid condition configuration.";

        if (!Supports(name) || !WiredLegacyProtocol.IsWithinLimits(proposed)) {
            return false;
        }

        name = NegativeNames.GetValueOrDefault(name, name).ToLowerInvariant();
        var p = proposed.IntParams;
        bool Range(int i, int low, int high) => p[i] >= low && p[i] <= high;
        bool F(int i) => p[i] is 0 or 100 or 200 or 201;
        bool U(int i) => p[i] is 0 or 10 or 11 or 200 or 201;
        bool Q(int i) => Range(i, 0, 1);
        var furni = ImmutableDictionary.CreateBuilder<string, int>();
        var users = ImmutableDictionary.CreateBuilder<string, int>();
        var secondary = proposed.SecondarySelectedItems;

        switch (name) {
            case "wf_cnd_actor_dir":
                if (p.Length != 3 || !Range(0, 0, 255) || !U(1) || !Q(2)) {
                    return false;
                }

                users["users"] = p[1];
                break;
            case "wf_cnd_actor_in_group":
                if (p.Length != 4 || !U(0) || !Q(1) || p[2] < 0 || !Q(3)) {
                    return false;
                }

                users["users"] = p[0];
                break;
            case "wf_cnd_actor_in_team":
            case "wf_cnd_has_handitem":
            case "wf_cnd_wearing_effect":
                if (p.Length != 3 || p[0] < 0 || name == "wf_cnd_actor_in_team" && p[0] > 4 || !U(1) || !Q(2)) {
                    return false;
                }

                users["users"] = p[1];
                break;
            case "wf_cnd_wearing_badge":
                if (p.Length != 2 || !U(0) || !Q(1) || proposed.Text.Length > 64) {
                    return false;
                }

                users["users"] = p[0];
                break;
            case "wf_cnd_user_performs_action":
                if (p.Length != 7 || !Range(0, 1, 11) || !Q(1) || !Range(2, 0, 17)
                    || !Q(3) || !Range(4, 0, 4) || !U(5) || !Q(6)) {
                    return false;
                }

                users["users"] = p[5];
                break;
            case "wf_cnd_triggerer_match":
                if (p.Length != 5 || !Range(0, 1, 7) || !Q(1) || !U(2)
                    || !(U(3) || p[3] == 101) || !Q(4) || proposed.Text.Length > 64) {
                    return false;
                }

                users["users"] = p[2];
                users["comparison"] = p[3];
                break;
            case "wf_cnd_trggrer_on_frn":
                if (p.Length != 3 || !F(0) || !U(1) || !Q(2)) {
                    return false;
                }

                furni["items"] = p[0];
                users["users"] = p[1];
                break;
            case "wf_cnd_furnis_hv_avtrs":
            case "wf_cnd_has_furni_on":
                if (p.Length != 2 || !Q(0) || !F(1)) {
                    return false;
                }

                furni["items"] = p[1];
                break;
            case "wf_cnd_match_snapshot":
                if (p.Length != 6 || Enumerable.Range(0, 4).Any(i => !Q(i)) || !F(4) || !Q(5)) {
                    return false;
                }

                furni["items"] = p[4];
                break;
            case "wf_cnd_stuff_is":
                if (p.Length != 3 || !F(0) || !F(1) || !Q(2)) {
                    return false;
                }

                var ids = ImmutableArray.CreateBuilder<uint>();

                foreach (var token in proposed.Text.Split([';', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                    if (ids.Count >= 100 || !uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0) {
                        return false;
                    }

                    ids.Add(id);
                }

                secondary = ids.Distinct().ToImmutableArray();
                furni["items"] = p[0];
                furni["comparison"] = p[1];
                break;
            case "wf_cnd_has_altitude":
                if (p.Length != 3 || !Range(0, 0, 2) || !F(1) || !Q(2)
                    || !WiredRoomOperations.TryAltitude(proposed.Text, out _)) {
                    return false;
                }

                furni["items"] = p[1];
                break;
            case "wf_cnd_valid_moves":
                if (p.Length != 0) {
                    return false;
                }

                furni["items"] = proposed.SelectedItems.Length > 0 ? 100 : 0;
                break;
            case "wf_cnd_slc_quantity":
                if (p.Length != 4 || !Range(0, 0, 2) || p[1] < 0 || !Q(2) || (p[2] == 0 ? !U(3) : !F(3))) {
                    return false;
                }

                if (p[2] == 0) {
                    users["users"] = p[3];
                }
                else {
                    furni["items"] = p[3];
                }

                break;
            case "wf_cnd_user_count_in":
                if (p.Length != 3 || p[0] < 0 || p[1] < p[0] || !U(2)) {
                    return false;
                }

                users["users"] = p[2];
                break;
            case "wf_cnd_team_has_rank":
                if (p.Length != 4 || !Range(0, 0, 4) || !Range(1, 0, 3) || !U(2) || !Q(3)) {
                    return false;
                }

                users["users"] = p[2];
                break;
            case "wf_cnd_team_has_score":
                if (p.Length != 5 || !Range(0, 0, 4) || !Range(1, 0, 2) || p[2] < 0 || !U(3) || !Q(4)) {
                    return false;
                }

                users["users"] = p[3];
                break;
            case "wf_cnd_counter_time_matches":
                if (p.Length != 5 || !Range(0, 0, 2) || !Range(1, 0, 99) || !Range(2, 0, 119) || !F(3) || !Q(4)) {
                    return false;
                }

                furni["items"] = p[3];
                break;
            case "wf_cnd_time_less_than":
            case "wf_cnd_time_more_than":
                if (p.Length != 1 || p[0] < 0) {
                    return false;
                }

                break;
            case "wf_cnd_date_rng_active":
                if (p.Length != 2 || p[0] < 0 || p[1] < 0 || p[1] != 0 && p[1] < p[0]) {
                    return false;
                }

                break;
            case "wf_cnd_match_time":
                if (p.Length != 9 || !Range(0, 0, 2) || !Range(3, 0, 2) || !Range(6, 0, 2)
                    || !Range(1, 0, 23) || !Range(2, 0, 23) || !Range(4, 0, 59) || !Range(5, 0, 59)
                    || !Range(7, 0, 59) || !Range(8, 0, 59)) {
                    return false;
                }

                break;
            case "wf_cnd_match_date":
                if (p.Length != 8 || !Range(0, 0, 127) || !Range(1, 0, 2) || !Range(2, 1, 31) || !Range(3, 1, 31)
                    || !Range(4, 0, 4095) || !Range(5, 0, 2) || !Range(6, 0, 9999) || !Range(7, 0, 9999)) {
                    return false;
                }

                break;
            default:
                return false;
        }

        validated = proposed with { FurniSources = furni.ToImmutable(), UserSources = users.ToImmutable(), SecondarySelectedItems = secondary };
        error = "";

        return true;
    }
}
