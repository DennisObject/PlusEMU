using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>Reads established Plus settings for the current editor. Conversion never mutates or captures current states.</summary>
public static class WiredLegacyConfigurationAdapter
{
    public static bool TryConvert(IWiredItem original, WiredBoxDescriptor descriptor, out WiredConfiguration configuration)
    {
        configuration = new();

        if (original is IWiredConfiguredItem configured)
        {
            configuration = configured.Configuration;

            return true;
        }

        var picked = original.SetItems?.Keys.Order().ToImmutableArray() ?? [];
        var source = picked.Length > 0 ? 100 : 0;
        var text = original.StringData ?? "";
        var fields = text.Split(';');
        int Field(int index, int fallback = 0) => index < fields.Length && int.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        var delay = original is IWiredCycle cycle ? Math.Max(0, cycle.Delay) : 0;
        ImmutableArray<int> parameters;
        var snapshots = ImmutableArray<WiredFurniSnapshot>.Empty;

        switch (descriptor.CanonicalName)
        {
            case "wf_act_kick_user":
                parameters = [0];
                break;
            case "wf_act_mute_triggerer":
                parameters = [Field(0), 0];
                text = text.Contains(';') ? text[(text.IndexOf(';') + 1)..] : "";
                break;
            case "wf_act_join_team":
                parameters = [2, Field(0, 1), 0, 0];
                text = "";
                break; // Old Plus joined Freeze teams.
            case "wf_act_leave_team":
                parameters = [0];
                text = "";
                break;
            case "wf_act_bot_teleport":
            case "wf_act_bot_move":
                parameters = [source, 100];
                break;
            case "wf_act_bot_clothes":
                parameters = [100];
                break; // Old Plus already stores name TAB figure.
            case "wf_act_bot_follow_avatar":
                parameters = [Field(0), 0, 100];
                text = text.Contains(';') ? text[(text.IndexOf(';') + 1)..] : "";
                break;
            case "wf_act_bot_give_handitem":
                parameters = [Field(1), 0, 100];
                text = fields[0];
                break;
            case "wf_act_bot_talk":
                // Old Plus only retained a bot name; its unfinished save/executor never retained or spoke text.
                parameters = [0, 100, -1];
                text += "\t";
                break;
            case "wf_act_give_reward":
                // The old Plus reward box never stored rewards. A nonempty custom row has no known schema.
                if (text.Length != 0)
                {
                    return false;
                }

                parameters = [0, 0, 0, 1, 0];
                break;
            case "wf_trg_says_something":
                parameters = [0, 1, original.BoolData ? 1 : 0];
                break;
            case "wf_trg_enter_room":
                parameters = [];
                break;
            case "wf_trg_periodically":
                parameters = [Math.Clamp(delay, 1, WiredTriggerConfiguration.MaxTimedUnits(descriptor.CanonicalName))];
                text = "";
                delay = 0;
                break;
            case "wf_trg_stuff_state":
                parameters = [0, source];
                text = "";
                break;
            case "wf_trg_walks_on_furni":
            case "wf_trg_walks_off_furni":
                parameters = [source];
                text = "";
                break;
            case "wf_trg_game_starts":
            case "wf_trg_game_ends":
            case "wf_trg_collision":
                parameters = [];
                text = "";
                break;
            case "wf_act_show_message":
                parameters = [0, 0, 34, -1];
                break;
            case "wf_act_teleport_to":
                parameters = [0, source, 0];
                text = "";
                break;
            case "wf_act_toggle_state":
                parameters = [0, source];
                text = "";
                break;
            case "wf_act_move_rotate":
                parameters = [Field(0) switch { 0 => -1, 1 => 8, 2 => 9, 3 => 10, 4 => 0, 5 => 2, 6 => 4, 7 => 6, _ => -1 }, Field(1) switch { 1 => 2, 2 => 4, 3 => 6, _ => 0 }, source, 0];
                text = "";
                break;
            case "wf_act_call_stacks":
                parameters = [source];
                text = "";
                break;
            case "wf_act_chase":
            case "wf_act_flee":
                parameters = [source];
                text = "";
                break;
            case "wf_act_match_to_sshot":
                parameters = [Field(0), Field(1), Field(2), Field(2), source];
                text = "";

                if (!TrySavedSnapshots(original, out snapshots))
                {
                    return false;
                }

                break;
            case "wf_cnd_match_snapshot":
            case "wf_cnd_not_match_snap":
                parameters = [Field(0), Field(1), Field(2), Field(2), source, 0];
                text = "";

                if (!TrySavedSnapshots(original, out snapshots))
                {
                    return false;
                }

                break;
            case "wf_cnd_user_count_in":
            case "wf_cnd_not_user_count":
                parameters = [Field(0), Field(1), 0];
                text = "";
                break;
            case "wf_cnd_trggrer_on_frn":
            case "wf_cnd_not_trggrer_on":
                parameters = [source, 0, 0];
                text = "";
                break;
            case "wf_cnd_furnis_hv_avtrs":
            case "wf_cnd_not_hv_avtrs":
            case "wf_cnd_has_furni_on":
            case "wf_cnd_not_furni_on":
                parameters = [1, source];
                text = "";
                break;
            case "wf_cnd_actor_in_group":
            case "wf_cnd_not_in_group":
                parameters = [0, 0, 0, 0];
                text = "";
                break;
            case "wf_cnd_wearing_badge":
            case "wf_cnd_not_wearing_b":
                parameters = [0, 0];
                break;
            case "wf_cnd_wearing_effect":
            case "wf_cnd_not_wearing_fx":
            case "wf_cnd_has_handitem":
            case "wf_cnd_actor_in_team":
                parameters = [Field(0), 0, 0];
                text = "";
                break;
            default:
                return false;
        }

        configuration = new()
        {
            IntParams = parameters,
            Text = text,
            SelectedItems = picked,
            Delay = delay,
            Snapshots = snapshots
        };

        return WiredLegacyProtocol.IsWithinLimits(configuration);
    }

    private static bool TrySavedSnapshots(IWiredItem original, out ImmutableArray<WiredFurniSnapshot> snapshots)
    {
        snapshots = [];
        var result = ImmutableArray.CreateBuilder<WiredFurniSnapshot>();

        foreach (var entry in (original.ItemsData ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = entry.IndexOf(':');

            if (colon < 1 || !uint.TryParse(entry[..colon], out var id))
            {
                return false;
            }

            var data = entry[(colon + 1)..].Split(',', 5);

            if (data.Length != 5 || !int.TryParse(data[0], out var x) || !int.TryParse(data[1], out var y)
                || !double.TryParse(data[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)
                || !double.IsFinite(z) || !int.TryParse(data[3], out var rotation))
            {
                return false;
            }

            var definition = original.SetItems?.GetValueOrDefault(id)?.Definition.Id ?? 0;
            result.Add(new(id, definition, x, y, z, rotation, data[4]));
        }

        snapshots = result.ToImmutable();

        return true;
    }
}
