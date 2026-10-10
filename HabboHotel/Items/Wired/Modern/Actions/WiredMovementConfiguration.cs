using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Validates the current Volt editors and decodes named source roles without changing wire fields.</summary>
public static class WiredMovementConfiguration
{
    // wf_act_move_rotate: index = current Volt editor option, value = stored direction/turn.
    // AIR's move/rotate radios: 4 ↗ 8 → 5 ↘ 9 ↓ 6 ↙ 10 ← 7 ↖ 11 ↑ (icons move_0..move_7).
    private static readonly int[] EditorMovement = [-1, 8, 9, 10, 0, 2, 4, 6, 1, 3, 5, 7];
    private static readonly int[] EditorRotation = [0, 2, 4, 6];

    /// <summary>Shows stored four-field move/rotate settings in the three-field editor order they were saved from.</summary>
    public static WiredConfiguration ForEditor(string name, WiredConfiguration stored)
    {
        if (!name.Equals("wf_act_move_rotate", StringComparison.OrdinalIgnoreCase)
            || stored.IntParams.IsDefault || stored.IntParams.Length != 4) {
            return stored;
        }

        var p = stored.IntParams;
        var movement = Array.IndexOf(EditorMovement, p[0]);
        var rotation = Array.IndexOf(EditorRotation, p[1]);

        // Validation admits only values the editor can show; anything else is sent as stored.
        if (movement < 0 || rotation < 0 || p[3] != 0) {
            return stored;
        }

        return stored with { IntParams = [movement, rotation, p[2]] };
    }

    public static bool TryValidate(string name, WiredConfiguration proposed,
        out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Invalid movement configuration.";

        if (!WiredMovementActions.Names.Contains(name) || !WiredLegacyProtocol.IsWithinLimits(proposed) || proposed.Version != WiredConfiguration.CurrentVersion
            || proposed.Delay is < 0 or > 20 || proposed.IntParams.IsDefault
            || proposed.SelectedItems.IsDefault || proposed.SecondarySelectedItems.IsDefault
            || proposed.Snapshots.IsDefault || proposed.Text == null
            || proposed.SelectedItems.Length > 100 || proposed.SecondarySelectedItems.Length > 100
            || proposed.SelectedItems.Any(id => id == 0) || proposed.SecondarySelectedItems.Any(id => id == 0)
            || proposed.Text.Length > 4000) {
            return false;
        }

        var p = proposed.IntParams;
        bool Range(int index, int min, int max) => p[index] >= min && p[index] <= max;
        bool Source(int index) => p[index] is 0 or 100 or 200 or 201;
        bool Users(int index) => p[index] is 0 or 11 or 200 or 201;
        var furni = ImmutableDictionary.CreateBuilder<string, int>();
        var users = ImmutableDictionary.CreateBuilder<string, int>();
        var secondary = proposed.SecondarySelectedItems;

        switch (name.ToLowerInvariant()) {
            case "wf_act_rel_mov":
                if (p.Length != 5 || !Range(0, 0, 1) || !Range(1, 0, 20)
                    || !Range(2, 0, 1) || !Range(3, 0, 20) || !Source(4)) {
                    return false;
                }

                furni["movers"] = p[4];
                break;
            case "wf_act_set_altitude":
                if (p.Length != 2 || !Range(0, 0, 2) || !Source(1)
                    || !WiredRoomOperations.TryAltitude(proposed.Text, out var altitude) || altitude > 40) {
                    return false;
                }

                furni["movers"] = p[1];
                break;
            case "wf_act_move_rotate":
                // Stored form is [direction, turn, source, 0]; it holds only what the three-field editor can show.
                if (p.Length == 3 ? !Range(0, -1, 11) || !Range(1, -1, 3)
                    : p.Length != 4 || !Range(0, -1, 10) || !EditorRotation.Contains(p[1]) || p[3] != 0) {
                    return false;
                }

                if (!Source(2)) {
                    return false;
                }

                // Rows saved before the stored form could hold rotation -1 (none picked), which always meant no rotation.
                if (p.Length == 3) {
                    proposed = proposed with { IntParams = [p[0] < 0 ? -1 : EditorMovement[p[0]], p[1] < 0 ? 0 : EditorRotation[p[1]], p[2], 0] };
                }

                furni["movers"] = p[2];
                break;
            case "wf_act_move_furni_as_group":
                if (p.Length != 2 || !Range(0, 0, 7) || !Source(1)) {
                    return false;
                }

                furni["movers"] = p[1];
                break;
            case "wf_act_furni_to_furni":
                if (p.Length != 2 || !Source(0) || !Source(1)
                    || !TryItemIds(proposed.Text, out secondary)) {
                    return false;
                }

                furni["movers"] = p[0];
                furni["targets"] = p[1];
                break;
            case "wf_act_furni_to_user":
                if (p.Length != 2 || !Source(0) || !Users(1)) {
                    return false;
                }

                furni["movers"] = p[0];
                users["users"] = p[1];
                break;
            case "wf_act_move_furni_to":
                if (p.Length != 3 || p[0] is not (0 or 2 or 4 or 6)
                    || !Range(1, 1, 5) || !Source(2)) {
                    return false;
                }

                // This legacy editor moves the event's furni toward resolved target picks.
                furni["movers"] = 0;
                furni["targets"] = p[2];
                break;
            case "wf_act_match_to_sshot":
                if (p.Length != 5 || Enumerable.Range(0, 4).Any(index => !Range(index, 0, 1)) || !Source(4)
                    || proposed.Snapshots.Any(snapshot => snapshot == null || !double.IsFinite(snapshot.Z)
                        || snapshot.Z is < 0 or > 80)) {
                    return false;
                }

                furni["movers"] = p[4];
                break;
            case "wf_act_toggle_state":
                if (p.Length != 2 || !Range(0, 0, 1) || !Source(1)) {
                    return false;
                }

                furni["movers"] = p[1];
                break;
            case "wf_act_toggle_to_rnd":
                if (p.Length != 1 || !Source(0)) {
                    return false;
                }

                furni["movers"] = p[0];
                break;
            case "wf_act_teleport_to":
                if (p.Length != 3 || !Range(0, 0, 1) || !Source(1) || !Users(2)) {
                    return false;
                }

                furni["targets"] = p[1];
                users["users"] = p[2];
                break;
            case "wf_act_user_to_furni":
                if (p.Length != 3 || !Source(0) || !Users(1) || !Range(2, 0, 2)) {
                    return false;
                }

                furni["targets"] = p[0];
                users["users"] = p[1];
                break;
            default:
                return false;
        }

        validated = proposed with
        {
            FurniSources = furni.ToImmutable(),
            UserSources = users.ToImmutable(),
            SelectedItems = proposed.SelectedItems.Distinct().ToImmutableArray(),
            SecondarySelectedItems = secondary.Distinct().ToImmutableArray()
        };
        error = string.Empty;

        return true;
    }

    private static bool TryItemIds(string text, out ImmutableArray<uint> ids)
    {
        var builder = ImmutableArray.CreateBuilder<uint>();

        foreach (var field in text.Split([';', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (builder.Count >= 100 || !uint.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0) {
                ids = [];

                return false;
            }

            builder.Add(id);
        }

        ids = builder.ToImmutable();

        return true;
    }
}
