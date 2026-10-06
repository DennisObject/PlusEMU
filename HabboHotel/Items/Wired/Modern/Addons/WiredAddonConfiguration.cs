using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;

namespace Plus.HabboHotel.Items.Wired.Modern.Addons;

public static class WiredAddonConfiguration
{
    public static WiredConfiguration Normalize(string name, WiredConfiguration c)
    {
        if (!WiredAddonModule.Names.Contains(name, StringComparer.Ordinal)) {
            throw new ArgumentException("Not a supported non-variable addon", nameof(name));
        }

        int P(int index, int fallback = 0) => WiredSelectorSources.Param(c, index, fallback);
        int B(int index) => P(index) == 1 ? 1 : 0;
        int Range(int index, int min, int max, int fallback = 0) => Math.Clamp(P(index, fallback), min, max);
        int Source(int index, bool users = false, bool all = false) => P(index) switch
        {
            0 or 200 or 201 => P(index),
            100 when !users => 100,
            11 when users => 11,
            900 when all => 900,
            _ => 0
        };
        int TextFallback(int fallback) => c.IntParams.IsEmpty && int.TryParse(c.Text, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) ? value : P(0, fallback);
        var text = c.Text;
        int[] fields;

        switch (name) {
            case "wf_xtra_anim_time":
                fields = [Math.Clamp(TextFallback(500), 50, 2000)];
                break;
            case "wf_xtra_filter_furni":
            case "wf_xtra_filter_users":
                fields = [Math.Clamp(TextFallback(0), 0, 10000)];
                break;
            case "wf_xtra_execution_limit":
                fields = [Range(0, 1, 100, 1), (Math.Clamp(P(1, 1000), 1000, 10000) + 250) / 500 * 500];
                break;
            case "wf_xtra_random":
                fields = [Range(0, 1, 1000, 1), Range(1, 0, 1000)];
                break;
            case "wf_xtra_mov_carry_users":
                fields = [Range(0, 0, 1), Source(1, true, true)];
                break;
            case "wf_xtra_mov_physics":
                fields = [B(0), B(1), B(2), B(3), Source(4, all: true), Source(5, all: true), Source(6, true, true)];
                break;
            case "wf_xtra_or_eval": {
                    var mode = P(0) is >= 0 and <= 6 ? P(0) : 0;
                    fields = [mode, Source(1), Range(2, mode == 4 ? 1 : 0, 100, 1)];
                    break;
                }
            case "wf_xtra_text_output_furni_name":
            case "wf_xtra_text_output_username": {
                    fields = [P(0, 1) == 2 ? 2 : 1, Source(1, name.EndsWith("username", StringComparison.Ordinal))];
                    var parts = text.Split('\t', 2);
                    var token = parts[0].Trim();

                    if (token.StartsWith("$(", StringComparison.Ordinal) && token.EndsWith(')')) {
                        token = token[2..^1].Trim();
                    }

                    token = token[..Math.Min(32, token.Length)];
                    var delimiter = parts.Length > 1 ? parts[1] : ", ";
                    text = token + "\t" + delimiter[..Math.Min(16, delimiter.Length)];
                    break;
                }
            case "wf_xtra_mov_curve":
                fields = [Math.Clamp(TextFallback(7), 0, 7), Range(1, 0, 100, 100), Range(2, -1000, 1000, 80),
                    B(3), Range(4, 0, 3), Source(5, true), Source(6)];
                break;
            case "wf_xtra_rotate_to_dir":
                fields = [B(0), Range(1, 0, 3), B(2), B(3), Range(4, 1, 100000, 1), Range(5, 0, 3),
                    B(6), B(7), B(8), Range(9, 0, 100000), Range(10, 0, 7), Range(11, 0, 127), B(12), B(13),
                    Range(14, 0, 2), B(15), Range(16, -64, 64), Range(17, 0, 3), Range(18, -1000, 1000),
                    Source(19, true), Source(20), Source(21, true), Source(22), Source(23, true)];
                break;
            default:
                fields = [];
                break;
        }

        return c with { IntParams = fields.ToImmutableArray(), Text = text };
    }
}
