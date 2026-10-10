using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables.Fx;

/// <summary>
/// The AIR variable FX form: source (0 furni, 1 user), visibility, show mode and duration, then style/colour/width/renderer from the
/// shared catalog, the value range, range-override and audience variables, segments, and for levelling progress its sub-renderer or
/// for number displays the icon alignment (the icon rides the text). Variable ids are [range minimum, range maximum, audience].
/// </summary>
public static class WiredVariableFxSettings
{
    private static readonly HashSet<string> Icons = ["energy", "shield", "magic", "food", "stamina", "poison", "mana", "health", "gold", "gems", "honor",
        "reputation", "cooldown", "timeleft", "burning", "freezing", "battery", "repairing", "stealth", "upgrading", "star_power", "droplet",
        "cash", "eye", "fish", "wooden_logs", "misc_heart", "misc_skull", "misc_star"];

    public static int Category(string name) => name switch
    {
        "wf_xtra_var_fx_health" => 0,
        "wf_xtra_var_fx_progress" => 1,
        "wf_xtra_var_fx_level" => 2,
        "wf_xtra_var_fx_status" => 3,
        "wf_xtra_var_fx_boss" => 4,
        "wf_xtra_var_fx_number" => 5,
        _ => -1
    };

    /// <summary>The form's own defaults for a category: user source, its first style and that style's default options.</summary>
    public static ImmutableArray<int> Defaults(int category)
    {
        var style = WiredVariableFxStyles.All[(category, 0)];
        int[] ints = [1, 2, 0, 0, 0, 3000, 0, style.DefaultColor, style.DefaultWidth, style.DefaultRenderer, 0, 0, 0, 100, 0, 0, 1, 1, 0, 0, 0];

        return category switch
        {
            2 => [.. ints, style.SubRenderers[0]],
            5 => [.. ints, 0],
            _ => [.. ints]
        };
    }

    public static bool UsesUserSource(WiredConfiguration configuration) => configuration.IntParams.Length > 0 && configuration.IntParams[0] == 1;

    public static bool TryDecode(string name, int itemId, WiredConfiguration configuration, WiredVariableReference shownVariable,
        Func<string, WiredVariableTarget, WiredVariableReference?>? resolve, out WiredVariableFxBinding? binding, out string error)
    {
        binding = null;
        error = "Invalid variable FX settings.";
        var category = Category(name);
        var p = configuration.IntParams;

        if (category < 0 || itemId <= 0 || p.Length != (category is 2 or 5 ? 22 : 21) || p[0] is not (0 or 1) || configuration.VariableIds.Length != 3) {
            return false;
        }

        var user = p[0] == 1;
        var target = user ? WiredVariableTarget.User : WiredVariableTarget.Furni;
        var visibility = p[1];
        var min = ((long)p[10] << 32) | (uint)p[11];
        var max = ((long)p[12] << 32) | (uint)p[13];
        var audienceValue = ((long)p[18] << 32) | (uint)p[19];

        if (shownVariable.Target != target || visibility is < 0 or > 4 || !user && visibility < 2 || p[2] is < 0 or > 2 || p[3] is < 0 or > 15
            || p[4] is not (0 or 1) || p[5] is < 1500 or > 20000 || !WiredVariableFxStyles.TryGet(category, p[6], out var style)) {
            return false;
        }

        // Levelling and number displays always span 0..100; every other category needs a real range.
        var range = category is 2 or 5 ? min == 0 && max == 100 : max > min;

        if (!style.Colors.Contains(p[7]) || !style.Widths.Contains(p[8]) || !style.Renderers.Contains(p[9]) || !range
            || p[14] is not (0 or 1) || p[15] is not (0 or 1) || p[16] != -10 && p[16] != p[0] || p[17] != -10 && p[17] != p[0]
            || audienceValue is < int.MinValue or > int.MaxValue || p[20] is < 0 or > 100) {
            return false;
        }

        var subRenderer = category == 2 ? p[21] : 0;
        var segmentRenderer = category == 2 && p[9] == 20 ? subRenderer : p[9];

        if (p[20] > 0 && segmentRenderer is not (2 or 4 or 13) || category == 2 && !style.SubRenderers.Contains(subRenderer)
            || category == 5 && p[21] is < 0 or > 2 || category != 5 && configuration.Text.Length != 0
            || category == 5 && configuration.Text.Length != 0 && !Icons.Contains(configuration.Text)) {
            return false;
        }

        var ids = configuration.VariableIds;
        bool Present(int index) => !WiredVariableAbsent.Is(ids[index]);

        if (p[14] == 1 && !Present(0) || p[15] == 1 && !Present(1) || visibility >= 3 != Present(2) || visibility < 3 && audienceValue != 0) {
            return false;
        }

        WiredVariableReference? Reference(int index, WiredVariableTarget expected)
        {
            if (!Present(index)) {
                return null;
            }

            if (resolve is not null) {
                return resolve(ids[index], expected);
            }

            return WiredVariableDescription.TryParseCatalogId(ids[index], out var parsed, out var token) && parsed == expected ? new(parsed, token) : null;
        }

        var minimum = p[14] == 1 ? Reference(0, p[16] == -10 ? WiredVariableTarget.Global : target) : null;
        var maximum = p[15] == 1 ? Reference(1, p[17] == -10 ? WiredVariableTarget.Global : target) : null;
        var audience = visibility >= 3 ? Reference(2, WiredVariableTarget.User) : null;

        if (p[14] == 1 && minimum is null || p[15] == 1 && maximum is null || visibility >= 3 && audience is null) {
            return false;
        }

        var extra = style.Extra.ToBuilder();

        if (p[20] > 0) {
            extra["segments"] = p[20].ToString(CultureInfo.InvariantCulture);
        }

        if (category == 2) {
            extra["sub_renderer"] = subRenderer.ToString(CultureInfo.InvariantCulture);
        }
        else if (category == 5) {
            if (configuration.Text.Length > 0) {
                extra["icon"] = configuration.Text;
            }

            extra["icon_alignment"] = p[21] switch { 1 => "right", 2 => "double", _ => "left" };
        }

        var config = new WiredVariableFxConfig(itemId, user, p[2], p[5], category, p[6], p[7], p[8], p[9], min, max, extra.ToImmutable());
        binding = new(config, shownVariable, visibility, audience, (int)audienceValue, minimum, maximum);
        error = "";

        return true;
    }
}
