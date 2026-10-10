using System.Collections.Immutable;
using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Items.Wired.Configuration;

internal static partial class WiredNativeAuxiliaryEditor
{
    private static WiredConfiguration? Metadata(string name, WiredNativeEditorConfiguration n, ImmutableArray<int> ints, string text)
    {
        try {
            return WiredVariableMetadataBox.Validate(name, Draft(n, ints, text));
        }
        catch (ArgumentException) {
            return null;
        }
    }

    private static IEnumerable<KeyValuePair<string, Spec>> VariableAddons()
    {
        // Variable and capturer name, plain-text or text-connector mode, over the context variable the capture writes.
        yield return new("wf_xtra_text_input_variable", new(Meta(0, 0, [0], 1), n =>
        {
            var name = WiredVariableTextInputBox.NormalizeName(n.Text);

            return Shape(n, 1, 0, 0, 1) && n.OwnedIntParams[0] is 0 or 1
                ? Draft(n, [n.OwnedIntParams[0] == 1 ? 2 : 1], name ?? n.Text) : null;
        }));
        // Placeholder name with an optional separator, the picked variable's target and display mode, over one merged source.
        yield return new("wf_xtra_text_output_variable", new(Sources([FurniSources], [[0, 11, 200, 201]], [0, 1, 0], 1), n =>
        {
            var p = n.OwnedIntParams;

            if (!Shape(n, 3, 1, 1, 1) || p[0] is not (0 or 1) || p[2] is not (0 or 1) || !TryTarget(p[1], out var target)) {
                return null;
            }

            var parts = n.Text.Split('\t', 2);

            return Draft(n, [target, p[2] == 1 ? 2 : 1, p[0] == 1 ? 2 : 1, n.UserSourceTypes[0], n.FurniSourceTypes[0]],
                "\t" + parts[0] + "\t" + (parts.Length > 1 ? parts[1] : ""));
        }));
        yield return new("wf_xtra_filter_furni_by_var", new(Meta(1, 1, [1, 0, 0, 0], 2), FilterByVariable));
        yield return new("wf_xtra_filter_users_by_var", new(Meta(1, 1, [1, 0, 0, 1], 2), FilterByVariable));
        yield return new("wf_xtra_var_text_connector", new(Meta(0, 0, []), n => Shape(n, 0, 0, 0, 0)
            ? Metadata("wf_xtra_var_text_connector", n, [], n.Text) : null));
        yield return new("wf_xtra_var_time_util", new(Meta(0, 0, [0, 0]), n => Shape(n, 2, 0, 0, 0) && n.Text.Length == 0
            ? Metadata("wf_xtra_var_time_util", n, n.OwnedIntParams, "") : null));
        yield return new("wf_xtra_var_lvlup_system", new(Meta(0, 0, [3, 1, 100, 50]), LevelSystem));

        // The six display forms share one int layout (plus one extra int for levelling and number displays); the three variable
        // slots are the range-override minimum and maximum and the audience, and a number display's icon rides the text.
        foreach (var name in new[] { "wf_xtra_var_fx_health", "wf_xtra_var_fx_progress", "wf_xtra_var_fx_level", "wf_xtra_var_fx_status",
            "wf_xtra_var_fx_boss", "wf_xtra_var_fx_number" }) {
            var fx = name;
            var category = Plus.HabboHotel.Items.Wired.Variables.Fx.WiredVariableFxSettings.Category(fx);
            var defaults = Plus.HabboHotel.Items.Wired.Variables.Fx.WiredVariableFxSettings.Defaults(category);

            yield return new(fx, new(Meta(0, 0, defaults, 3), n => Shape(n, defaults.Length, 0, 0, 3)
                ? Metadata(fx, n, n.OwnedIntParams, n.Text) : null));
        }
    }

    // Count, sort order, literal-or-variable option and its target; the filtered variable and the operand variable.
    private static WiredConfiguration? FilterByVariable(WiredNativeEditorConfiguration n)
    {
        var p = n.OwnedIntParams;

        return Shape(n, 4, 1, 1, 2) && n.Text.Length == 0 && In(p[0], 1, 1000) && p[1] is >= 0 and <= 5 && p[2] is 0 or 1 && TryTarget(p[3], out var target)
            ? Draft(n, [p[1], p[2], p[0], target, n.UserSourceTypes[0], n.FurniSourceTypes[0]]) : null;
    }

    // Subvariable mask and mode (0 manual, 1 linear, 2 exponential) then the mode's own fields; manual keeps its text.
    private static WiredConfiguration? LevelSystem(WiredNativeEditorConfiguration n)
    {
        var p = n.OwnedIntParams;

        if (n.FurniSourceTypes.Length != 0 || n.UserSourceTypes.Length != 0 || n.VariableIds.Length != 0 || p.Length < 2
            || p[0] is < 0 or > 255 || p[1] is < 0 or > 2 || p.Length != p[1] switch { 0 => 2, 1 => 4, _ => 5 }
            || p[1] != 0 && n.Text.Length != 0) {
            return null;
        }

        var subvariables = Enumerable.Range(0, 8).Where(bit => (p[0] & 1 << bit) != 0).ToArray();
        object settings = p[1] switch
        {
            1 => new { mode = 1, stepSize = p[2], maxLevel = p[3], subvariables },
            2 => new { mode = 2, firstLevelXp = p[2], increaseFactor = p[3], maxLevel = p[4], subvariables },
            _ => new { mode = 3, interpolationText = n.Text.Replace("\r\n", "\n").Replace('\r', '\n'), maxLevel = 10, subvariables }
        };

        return p[1] != 0 && p[^1] is < 2 or > 10000 ? null : Metadata("wf_xtra_var_lvlup_system", n, [], JsonSerializer.Serialize(settings));
    }
}
