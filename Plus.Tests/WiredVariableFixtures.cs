using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Tests;

internal static class WiredVariableFixtures
{
    /// <summary>A with_var selector draft naming its variables by token, as the native catalog ids the runtime now reads.</summary>
    public static WiredConfiguration WithVar(string selector, WiredConfiguration draft)
    {
        var tokens = draft.Text.Split('\t');
        string Id(string token, int target) => token.Length == 0 ? "n"
            : (target switch { 0 => "user:", 1 => "furni:", 2 => "ctx:", _ => "room:" }) + (token.StartsWith("custom:", StringComparison.Ordinal) ? token[7..] : token);

        // The selector's own variable lives on the selected kind; the second id follows the operand target.
        return draft with { Text = "", VariableIds = [Id(tokens[0], selector == "wf_slc_furni_with_var" ? 1 : 0), Id(tokens.Length > 1 ? tokens[1] : "", draft.IntParams[4])] };
    }
}
