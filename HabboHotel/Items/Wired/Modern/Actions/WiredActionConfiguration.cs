using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public static class WiredActionConfiguration
{
    // Raw current-editor fields; the concrete box validates and decodes named roles before publication.
    public static WiredConfiguration Defaults(string name)
    {
        ImmutableArray<int> parameters = name switch
        {
            "wf_act_rel_mov" => [1, 0, 1, 0, 100], "wf_act_set_altitude" => [2, 100],
            "wf_act_move_rotate" => [0, 0, 100], "wf_act_move_furni_as_group" => [0, 100],
            "wf_act_furni_to_furni" => [0, 100], "wf_act_furni_to_user" => [100, 0],
            "wf_act_move_furni_to" => [0, 1, 100], "wf_act_match_to_sshot" => [0, 0, 0, 0, 100],
            "wf_act_toggle_state" => [0, 100], "wf_act_toggle_to_rnd" => [100],
            "wf_act_teleport_to" => [0, 100, 0], "wf_act_user_to_furni" => [100, 0, 1],
            "wf_act_control_clock" => [0, 100], "wf_act_adjust_clock" => [0, 100, 0, 0],
            "wf_act_reset_timers" => [], "wf_act_call_stacks" or "wf_act_neg_call_stacks" => [100],
            "wf_act_send_signal" or "wf_act_neg_send_signal" => [0, 100, 0, 0, 0, 0],
            "wf_act_log" or "wf_act_neg_log" => [1, 0], "wf_act_show_message" => [0, 0, 34, -1],
            "wf_act_click_conf" => [0, 0, 0],
            _ => throw new ArgumentException("Unknown action.", nameof(name))
        };
        return new() { IntParams = parameters, Text = name == "wf_act_set_altitude" ? "0" : "" };
    }
}
