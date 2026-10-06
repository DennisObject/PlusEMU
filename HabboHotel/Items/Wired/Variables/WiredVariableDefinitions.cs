using System.Text.Json;
using System.Text.RegularExpressions;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

public static class WiredVariableDefinitions
{
    public static bool Supports(string name) => name is "wf_var_user" or "wf_var_furni" or "wf_var_room"
        or "wf_var_context" or "wf_var_echo" or "wf_var_reference";
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
        var initial = 0;
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
                if (ints.Length != 2 || ints[0] is not (1 or 10 or 11)) {
                    return false;
                }

                target = WiredVariableTarget.Global;
                availability = (WiredVariableAvailability)ints[0];
                hasValue = true;
                initial = ints[1];
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
            case "wf_var_reference":
                try {
                    using var document = JsonDocument.Parse(text);
                    var root = document.RootElement;
                    text = root.GetProperty("variableName").GetString() ?? "";
                    target = (WiredVariableTarget)root.GetProperty("sourceTargetType").GetInt32();

                    if (!Enum.IsDefined(target)) {
                        return false;
                    }

                    var sourceRoom = name == "wf_var_reference" ? root.GetProperty("sourceRoomId").GetUInt32() : roomId;
                    var sourceToken = name == "wf_var_reference"
                        ? $"custom:{root.GetProperty("sourceVariableItemId").GetUInt32()}"
                        : root.GetProperty("sourceVariableToken").GetString() ?? "";

                    if (sourceRoom == 0 || sourceToken.Length == 0
                        || (name == "wf_var_reference" && (sourceRoom == roomId || target is not (WiredVariableTarget.User or WiredVariableTarget.Global)))) {
                        return false;
                    }

                    var readOnly = name == "wf_var_reference" && (!root.TryGetProperty("readOnly", out var readOnlyValue) || readOnlyValue.GetBoolean());
                    link = new(sourceRoom, new(target, sourceToken), readOnly);
                    availability = WiredVariableAvailability.RoomActive;
                    hasValue = true; // Actual source flags are enforced by resolution, never these labels.
                }
                catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) {
                    return false;
                }

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
