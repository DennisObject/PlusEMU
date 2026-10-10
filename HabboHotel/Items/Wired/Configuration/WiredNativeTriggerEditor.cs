using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;

namespace Plus.HabboHotel.Items.Wired.Configuration;

internal static class WiredNativeTriggerEditor
{
    internal static bool Supports(string name) => WiredTriggerConfiguration.Events.ContainsKey(name)
        && name is not ("wf_trg_says_something" or "wf_trg_walks_on_furni" or "wf_trg_periodically");

    internal static WiredNativeEditorMetadata Metadata(string name) => name switch
    {
        "wf_trg_walks_off_furni" or "wf_trg_click_furni" or "wf_trg_click_tile" or "wf_trg_stuff_state"
            or "wf_trg_recv_signal" => new([[0, 100, 200, 201]], [], [100], [], [], false),
        "wf_trg_state_changed" => new([[0, 100, 200, 201]], [], [100], [], [0], false),
        "wf_trg_clock_counter" => new([[0, 100, 200, 201]], [], [100], [], [0, 0, 0], false),
        "wf_trg_bot_reached_stf" => new([[0, 100, 200, 201]], [[0, 200, 201]], [100], [0], [], false),
        "wf_trg_bot_reached_avtr" => new([], [[0, 200, 201]], [], [0], [], false),
        "wf_trg_click_user" => new([], [], [], [], [0, 0], false),
        "wf_trg_score_achieved" => new([], [], [], [], [1, 0], false),
        "wf_trg_user_performs_action" => new([], [], [], [], [0], false),
        _ when WiredTriggerConfiguration.IsTimed(name) => new([], [], [], [], [1], false),
        _ when Supports(name) => new([], [], [], [], [], false),
        _ => throw new InvalidDataException("Unknown native trigger.")
    };

    internal static bool TryCompile(string name, WiredNativeEditorConfiguration native, out WiredConfiguration runtime)
    {
        runtime = new();
        if (!Supports(name) || native.Delay != null || native.Quantifier != null || !native.VariableIds.IsEmpty) {
            return false;
        }

        var p = native.OwnedIntParams;
        var f = native.FurniSourceTypes;
        var u = native.UserSourceTypes;
        ImmutableArray<int> parameters;
        switch (name) {
            case "wf_trg_enter_room":
            case "wf_trg_leave_room":
            case "wf_trg_game_starts":
            case "wf_trg_game_ends":
            case "wf_trg_collision":
                if (!p.IsEmpty) return false;
                parameters = [];
                break;
            case "wf_trg_walks_off_furni":
            case "wf_trg_click_furni":
            case "wf_trg_click_tile":
                if (!p.IsEmpty || f.Length != 1) return false;
                parameters = [f[0]];
                break;
            case "wf_trg_stuff_state":
            case "wf_trg_recv_signal":
                if (!p.IsEmpty || f.Length != 1) return false;
                parameters = [0, f[0]];
                break;
            case "wf_trg_state_changed":
                if (p.Length != 1 || f.Length != 1) return false;
                parameters = [p[0], f[0]];
                break;
            case "wf_trg_bot_reached_stf":
                if (!p.IsEmpty || f.Length != 1 || u.Length != 1) return false;
                parameters = [f[0], u[0]];
                break;
            case "wf_trg_bot_reached_avtr":
                if (!p.IsEmpty || u.Length != 1) return false;
                parameters = [u[0]];
                break;
            case "wf_trg_clock_counter":
                if (p.Length != 3 || f.Length != 1 || p[0] is < 0 or > 59 || p[2] is < 0 or > 1) return false;
                parameters = [p[1], p[0] * 2 + p[2], f[0]];
                break;
            case "wf_trg_click_user":
            case "wf_trg_score_achieved":
                if (p.Length != 2) return false;
                parameters = p;
                break;
            case "wf_trg_user_performs_action":
                if (p.Length != 1 || !TryAvatarAction(p[0], native.Text, out parameters)) return false;
                break;
            default:
                if (!WiredTriggerConfiguration.IsTimed(name) || p.Length != 1) return false;
                parameters = p;
                break;
        }

        if (name is not ("wf_trg_enter_room" or "wf_trg_leave_room" or "wf_trg_bot_reached_avtr"
            or "wf_trg_bot_reached_stf" or "wf_trg_user_performs_action") && native.Text.Length != 0) {
            return false;
        }

        var proposed = new WiredConfiguration {
            IntParams = parameters,
            Text = native.Text,
            SelectedItems = native.PrimaryItems.Select(item => item.ItemId).ToImmutableArray(),
            SecondarySelectedItems = native.SecondaryItems.Select(item => item.ItemId).ToImmutableArray(),
            Snapshots = native.SavedState.Snapshots
        };
        return WiredTriggerConfiguration.TryValidate(name, proposed, out runtime, out _);
    }

    internal static bool TryAvatarAction(int code, string text, out ImmutableArray<int> parameters)
    {
        parameters = [];
        var action = code switch {
            0 => 1, 1 => 2, 2 => 3, 3 => 12,
            >= 4 and <= 8 => code,
            10 => 9, 11 => 10, 67 => 11,
            _ => -1
        };
        if (action < 0 || text.Length != 0 && code is not (10 or 11)) return false;
        var value = code == 11 ? 1 : 0;
        if (text.Length != 0) {
            var raw = code == 11 && text.StartsWith("dance ", StringComparison.Ordinal) ? text[6..] : text;
            if (code == 11 && !text.StartsWith("dance ", StringComparison.Ordinal)
                || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                || value < 0 || value > (code == 11 ? 4 : 17) || code == 11 && value == 0) return false;
        }
        parameters = [action, code == 10 && text.Length != 0 ? 1 : 0, code == 10 ? value : 0,
            code == 11 && text.Length != 0 ? 1 : 0, code == 11 ? value : 1];
        return true;
    }
}
