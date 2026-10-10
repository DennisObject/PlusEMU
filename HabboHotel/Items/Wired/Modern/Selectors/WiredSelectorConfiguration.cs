using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

/// <summary>Decodes the active Volt/Polaris action envelope, not Turbo's compact selector fields.</summary>
public static class WiredSelectorConfiguration
{
    public static WiredConfiguration Normalize(string name, WiredConfiguration c)
    {
        if (!WiredSelectorModule.Names.Contains(name, StringComparer.Ordinal)) {
            throw new ArgumentException("Not a supported selector", nameof(name));
        }

        int P(int index, int fallback = 0) => WiredSelectorSources.Param(c, index, fallback);
        int Enum(int index, int min, int max, int fallback = 0) =>
            P(index, fallback) is var value && value >= min && value <= max ? value : fallback;
        int Source(int index, bool secondary = false, bool users = false) => P(index) switch
        {
            0 or 200 or 201 => P(index),
            100 when !secondary && !users => 100,
            11 when users => 11,
            101 when secondary => 101,
            _ => 0
        };

        // Volt uses retained picks only when an older layout omitted the source field.
        int AnchorSource(int index) => index >= c.IntParams.Length && !c.SelectedItems.IsEmpty ? 100 : Source(index);

        int[] fields = name switch
        {
            "wf_slc_furni_area" or "wf_slc_users_area" => [P(0), P(1), Math.Max(0, P(2)), Math.Max(0, P(3)), P(4), P(5)],
            "wf_slc_furni_bytype" => [Enum(0, 0, 2), P(1) == 1 ? 1 : 0, P(2), P(3)],
            "wf_slc_users_bytype" => [P(0, 1) is 1 or 2 or 4 ? P(0, 1) : 1, P(1), P(2)],
            "wf_slc_furni_onfurni" => [Enum(0, 0, 3), AnchorSource(1), P(2), P(3)],
            "wf_slc_users_onfurni" => [AnchorSource(0), P(1), P(2)],
            "wf_slc_users_team" => [Enum(0, 0, 4), P(1), P(2)],
            "wf_slc_users_handitem" => [Math.Max(0, P(0)), P(1), P(2)],
            "wf_slc_users_group" => [Enum(0, 0, 1), Math.Max(0, P(1)), P(2), P(3)],
            "wf_slc_users_byaction" => [Enum(0, 1, 11, 1), P(1) == 1 ? 1 : 0, P(2), P(3) == 1 ? 1 : 0, P(4, 1), P(5), P(6)],
            "wf_slc_furni_altitude" => [Enum(0, 0, 2, 1), P(1), P(2)],
            "wf_slc_furni_with_var" or "wf_slc_users_with_var" =>
                [P(0) == 1 ? 1 : 0, Enum(1, 0, 5, 2), P(2) == 1 ? 1 : 0, P(3), Enum(4, 0, 3), Source(5, users: true), Source(6, true), P(7), P(8)],
            "wf_slc_furni_neighborhood" or "wf_slc_users_neighborhood" => Neighborhood(c),
            _ => [P(0), P(1)]
        };
        var (filter, invert) = WiredSelectorModule.SwitchIndexes(name);
        fields[filter] = fields[filter] == 1 ? 1 : 0;
        fields[invert] = fields[invert] == 1 ? 1 : 0;
        var text = c.Text;

        if (name == "wf_slc_furni_altitude") {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) {
                value = 0;
            }

            text = Math.Max(0, value).ToString("R", CultureInfo.InvariantCulture);
        }

        return c with { IntParams = fields.ToImmutableArray(), Text = text };
    }

    private static int[] Neighborhood(WiredConfiguration c)
    {
        int P(int index) => WiredSelectorSources.Param(c, index);
        var count = P(5);

        if (count < 0 || count > 64 || count > 0 && c.IntParams.Length < 6 + count * 2) {
            throw new ArgumentException("Neighborhood requires complete offsets, at most 64 tiles");
        }

        var fields = new int[6 + count * 2];

        for (var i = 0; i < fields.Length; i++) {
            fields[i] = P(i);
        }

        fields[0] = fields[0] is >= 0 and <= 5 ? fields[0] : 0;

        return fields;
    }
}
