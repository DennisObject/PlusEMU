using System.Text.Json;
using System.Text.RegularExpressions;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

public static class WiredVariableDefinitions
{
    public static bool Supports(string name) => name is "wf_var_user" or "wf_var_furni" or "wf_var_room"
        or "wf_var_context" or "wf_var_echo" or "wf_var_reference" or "wf_var_quest" or "wf_var_quest_chain";
    /// <summary>The picker id of a variable shared from another room; the module still authorizes the source it names.</summary>
    internal static string SharedId(uint roomId, WiredVariableTarget target, uint definitionId) =>
        $"shared:{roomId}:{(target == WiredVariableTarget.Global ? "room" : "user")}:{definitionId}";

    internal static bool TryParseShared(string id, out uint roomId, out WiredVariableTarget target, out uint definitionId)
    {
        roomId = definitionId = 0;
        target = default;
        var parts = id.Split(':');

        if (parts.Length != 4 || parts[0] != "shared" || parts[2] is not ("user" or "room")
            || !uint.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out roomId) || roomId == 0
            || !uint.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out definitionId) || definitionId == 0
            || SharedId(roomId, parts[2] == "room" ? WiredVariableTarget.Global : WiredVariableTarget.User, definitionId) != id) {
            return false;
        }

        target = parts[2] == "room" ? WiredVariableTarget.Global : WiredVariableTarget.User;

        return true;
    }

    public static bool TryDecode(string name, uint itemId, uint roomId, uint ownerId, WiredConfiguration configuration,
        out WiredVariableDefinition? definition, out string error)
    {
        definition = null;
        error = "Invalid variable configuration.";

        if (itemId == 0 || roomId == 0 || ownerId == 0 || configuration.Version != 1) {
            return false;
        }

        var text = configuration.Text;
        var ints = configuration.IntParams;
        WiredVariableTarget target;
        WiredVariableAvailability availability;
        bool hasValue;
        var initial = 0L;
        WiredVariableLink? link = null;

        switch (name) {
            case "wf_var_user":
            case "wf_var_furni":
                if (ints.Length != 2 || ints[0] is not (0 or 1)) {
                    return false;
                }

                target = name == "wf_var_user" ? WiredVariableTarget.User : WiredVariableTarget.Furni;

                if (ints[1] != (target == WiredVariableTarget.User ? 0 : 1) && ints[1] != 10
                    && !(target == WiredVariableTarget.User && ints[1] == 11)) {
                    return false;
                }

                availability = (WiredVariableAvailability)ints[1];
                hasValue = ints[0] == 1;
                break;
            case "wf_var_room":
                // The AIR form edits availability only; the starting value is always zero and live values survive saves.
                if (ints.Length != 2 || ints[0] is not (1 or 10 or 11) || ints[1] != 0) {
                    return false;
                }

                target = WiredVariableTarget.Global;
                availability = (WiredVariableAvailability)ints[0];
                hasValue = true;
                break;
            case "wf_var_context":
                if (ints.Length != 1 || ints[0] is not (0 or 1)) {
                    return false;
                }

                target = WiredVariableTarget.Context;
                availability = WiredVariableAvailability.RoomActive;
                hasValue = ints[0] == 1;
                break;
            case "wf_var_echo":
                // An echo names one variable of this room; the picked catalog id carries its target and definition.
                if (configuration.VariableIds.Length != 1
                    || !WiredVariableDescription.TryParseCatalogId(configuration.VariableIds[0], out target, out var echoed)) {
                    return false;
                }

                link = new(roomId, new(target, echoed), false);
                availability = WiredVariableAvailability.RoomActive;
                hasValue = true; // Actual source flags are enforced by resolution, never these labels.
                break;
            case "wf_var_reference":
                // A reference names one shared variable of another room: "shared:{room}:{user|room}:{definition}".
                if (configuration.VariableIds.Length != 1 || ints.Length != 1 || ints[0] is not (0 or 1)
                    || !TryParseShared(configuration.VariableIds[0], out var sourceRoom, out target, out var sourceItem)
                    || sourceRoom == roomId) {
                    return false;
                }

                link = new(sourceRoom, new(target, $"custom:{sourceItem}"), ints[0] == 1);
                availability = WiredVariableAvailability.RoomActive;
                hasValue = true;
                break;
            case "wf_var_quest":
            case "wf_var_quest_chain":
                // A read-only user variable over the real quest tables; its own builtin token reads the player's quest state.
                if (ints.Length != 0 || configuration.VariableIds.Length != 0 || WiredQuestVariables.Binding(name, text.Contains('\t') ? text : "") is null) {
                    return false;
                }

                text = text.Split('\t')[0];
                target = WiredVariableTarget.User;
                availability = WiredVariableAvailability.RoomActive;
                hasValue = true;
                link = new(roomId, new(WiredVariableTarget.User, WiredQuestVariables.Token(itemId)), true);
                break;
            default:
                error = "This variable definition has no executable implementation.";

                return false;
        }

        if (text.Length is < 1 or > 40 || !Regex.IsMatch(text, "^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant)) {
            return false;
        }

        definition = new(itemId, roomId, ownerId, text, target, availability, hasValue, initial, link);
        error = "";

        return true;
    }
}
