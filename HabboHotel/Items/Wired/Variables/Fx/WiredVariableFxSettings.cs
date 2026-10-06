using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables.Fx;

public static class WiredVariableFxSettings
{
    private static readonly string[] StatusIcons = ["energy", "shield", "magic", "food", "stamina", "poison", "mana", "health", "gold", "gems", "honor", "reputation", "cooldown", "timeleft", "burning", "freezing", "battery", "repairing", "stealth", "upgrading", "star_power", "droplet"];
    private static readonly string?[] StatusColors = ["#ffd83d", "#4aa9f6", "#8751d1", "#ff9f24", "#86d213", "#8ddc35", "#268fff", "#7dce35", "#ffc83d", "#416bdd", "#fac384", "#ffd83d", "#b8c3cc", "#74b9e8", "#ff5a1f", "#82cfff", null, "#c9c5b8", "#6254a8", "#6bdc34", "#ffd900", "#4aabf5"];
    private static readonly HashSet<string> Icons = [.. StatusIcons, "cash", "eye", "fish", "wooden_logs", "misc_heart", "misc_skull", "misc_star"];
    private static readonly int[][] Renderers = [[10, 12, 13, 11], [0, 2, 3, 4, 1], [20, 21], Enumerable.Repeat(2, 22).ToArray(), [100, 100], [201, 200, 200]];

    public static bool TryDecode(string name, int itemId, WiredConfiguration configuration, WiredVariableReference shownVariable,
        out WiredVariableFxBinding? binding, out string error)
    {
        binding = null;
        error = "Invalid variable FX settings.";
        var category = name switch
        {
            "wf_xtra_var_fx_health" => 0,
            "wf_xtra_var_fx_progress" => 1,
            "wf_xtra_var_fx_level" => 2,
            "wf_xtra_var_fx_status" => 3,
            "wf_xtra_var_fx_boss" => 4,
            "wf_xtra_var_fx_number" => 5,
            _ => -1
        };
        var p = configuration.IntParams;

        if (category < 0 || itemId <= 0 || p.Length != 16 || p[0] is not (0 or 1) || p[1] is < 0 or > 4
            || p[2] is < 0 or > 2 || p[3] is < 1500 or > 20000 || p[4] < 0 || p[4] >= Renderers[category].Length
            || p[5] is not (>= -1 and <= 11 or 1001 or 1002) || p[6] is < -1 or > 4 || p[7] is < 0 or > 100
            || p[10] is not (0 or 1) || p[11] is not (0 or 1) || p[12] is not (0 or 1) || p[13] is not (0 or 1)) {
            return false;
        }

        var target = p[0] == 0 ? WiredVariableTarget.User : WiredVariableTarget.Furni;

        if (shownVariable.Target != target) {
            return false;
        }

        var tokens = configuration.Text.Split('\t');

        if (tokens.Length > 4) {
            return false;
        }

        var minToken = tokens.ElementAtOrDefault(0) ?? "";
        var maxToken = tokens.ElementAtOrDefault(1) ?? "";
        var audienceToken = tokens.ElementAtOrDefault(2) ?? "";

        if (p[10] == 1 && minToken.Length == 0 || p[11] == 1 && maxToken.Length == 0 || p[1] >= 3 && audienceToken.Length == 0) {
            return false;
        }

        var extra = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        if (p[7] > 0 && (Renderers[category][p[4]] is 1 or 2 or 3 or 4 or 13 || category == 2)) {
            extra["segments"] = p[7].ToString(CultureInfo.InvariantCulture);
        }

        if (category == 2) {
            if (p[15] is < -1 or > 4) {
                return false;
            }

            extra["sub_renderer"] = p[15].ToString(CultureInfo.InvariantCulture);
        }

        if (category == 3) {
            extra["icon"] = StatusIcons[p[4]];

            if (StatusColors[p[4]] is { } color) {
                extra["color"] = color;
            }

            extra["metallic"] = p[4] is 8 or 9 or 17 or 20 ? "true" : "false";
        }

        if (category == 4 && p[4] == 0) {
            extra["icon"] = "misc_skull";
            extra["icon_alignment"] = "double";
        }

        if (category == 5) {
            extra["design"] = new[] { "freeze_style", "shalimar", "blocky" }[p[4]];
            var icon = tokens.ElementAtOrDefault(3) ?? "";

            if (icon.Length > 0) {
                if (!Icons.Contains(icon) || p[15] is < 0 or > 2) {
                    return false;
                }

                extra["icon"] = icon;
                extra["icon_alignment"] = new[] { "left", "right", "double" }[p[15]];
            }
        }

        var min = category is 2 or 5 ? 0L : p[8];
        var max = category is 2 or 5 ? 100L : Math.Max((long)p[9], min + 1);
        var config = new WiredVariableFxConfig(itemId, p[0] == 0, p[2], p[3], category, p[4], p[5], p[6], Renderers[category][p[4]], min, max, extra.ToImmutable());
        binding = new(config, shownVariable, p[1], audienceToken.Length > 0 ? new(WiredVariableTarget.User, audienceToken) : null, p[14],
            p[10] == 1 ? new(p[12] == 1 ? WiredVariableTarget.Global : target, minToken) : null,
            p[11] == 1 ? new(p[13] == 1 ? WiredVariableTarget.Global : target, maxToken) : null);
        error = "";

        return true;
    }
}
