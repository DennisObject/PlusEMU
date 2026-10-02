using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Client editor drafts. Empty names and selections intentionally fail save/execution validation.</summary>
public static class WiredVariableDefaults
{
    public static WiredConfiguration Create(string name) => name switch
    {
        "wf_act_give_var" => new() { IntParams = [0, 0, 0, 0, 0] },
        "wf_act_remove_var" => new() { IntParams = [0, 0, 0] },
        "wf_act_change_var_val" => new() { IntParams = [0, 0, 0, 0, 0, 0, 0, 0, 0], Text = "\t\t" },
        "wf_cnd_has_var" or "wf_cnd_neg_has_var" => new() { IntParams = [0, 0, 0, 0] },
        "wf_cnd_var_val_match" => new() { IntParams = [0, 2, 0, 0, 0, 0, 0, 0, 0, 0], Text = "\t\t" },
        "wf_cnd_var_age_match" => new() { IntParams = [0, 0, 0, 0, 1, 0, 0, 0] },
        "wf_var_user" => new() { IntParams = [0, 0] },
        "wf_var_furni" => new() { IntParams = [0, 1] },
        "wf_var_room" => new() { IntParams = [1, 0] },
        "wf_var_context" => new() { IntParams = [0] },
        "wf_var_echo" => new() { Text = "{\"variableName\":\"\",\"sourceTargetType\":0,\"sourceVariableToken\":\"\"}" },
        "wf_var_reference" => new() { Text = "{\"variableName\":\"\",\"sourceTargetType\":0,\"sourceRoomId\":0,\"sourceVariableItemId\":0,\"readOnly\":true}" },
        _ => throw new ArgumentException("Unsupported variable box.", nameof(name))
    };
}
