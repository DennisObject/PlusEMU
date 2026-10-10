using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Configuration;

internal static partial class WiredNativeAuxiliaryEditor
{
    // The variable name is the native text; availability and value flags are the AIR form's own fields.
    private static IEnumerable<KeyValuePair<string, Spec>> Definitions()
    {
        // Availability (0 user, 10 persistent, 11 shared) then "has value"; the definition decoder takes them the other way round.
        yield return new("wf_var_user", new(Meta(0, 0, [0, 0]), n => Shape(n, 2, 0, 0, 0)
            ? Draft(n, [n.OwnedIntParams[1], n.OwnedIntParams[0]], n.Text) : null));
        // "Has value" then availability (1 room, 10 persistent).
        yield return new("wf_var_furni", new(Meta(0, 0, [0, 1]), n => Shape(n, 2, 0, 0, 0) ? Draft(n, n.OwnedIntParams, n.Text) : null));
        // Availability only; the room value lives in the variable store, not in the definition.
        yield return new("wf_var_room", new(Meta(0, 0, [1]), n => Shape(n, 1, 0, 0, 0) ? Draft(n, [n.OwnedIntParams[0], 0], n.Text) : null));
        yield return new("wf_var_context", new(Meta(0, 0, [0]), n => Shape(n, 1, 0, 0, 0) ? Draft(n, n.OwnedIntParams, n.Text) : null));
        // An echo names one variable of this room; the picked catalog id says which.
        yield return new("wf_var_echo", new(Meta(0, 0, [], 1), n => Shape(n, 0, 0, 0, 1) ? Draft(n, [], n.Text) : null));
        // Variable name and the exact quest (its action name) or chain (its category) read for each player; blank is the inactive default.
        yield return new("wf_var_quest", new(Meta(0, 0, []), QuestForm));
        yield return new("wf_var_quest_chain", new(Meta(0, 0, []), QuestForm));
        // A reference names one variable shared from another room, read-only unless unchecked.
        yield return new("wf_var_reference", new(Meta(0, 0, [1], 1), n => Shape(n, 1, 0, 0, 1) ? Draft(n, n.OwnedIntParams, n.Text) : null));
    }

    private static WiredConfiguration? QuestForm(WiredNativeEditorConfiguration n) =>
        Shape(n, 0, 0, 0, 0) && n.Text.Length <= 600 ? Draft(n, [], n.Text) : null;
}
