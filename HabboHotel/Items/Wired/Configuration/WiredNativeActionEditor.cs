using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>
/// Native editor mappings for Action boxes. Core owns bounds, source-set validation, origin binding and
/// persistence; this class only declares footer metadata and derives a plain runtime from a native DTO.
/// </summary>
internal static class WiredNativeActionEditor
{
    private static readonly int[] Furni = [0, 100, 200, 201];
    private static readonly int[] FurniAndSnapshot = [0, 100, 101, 200, 201];
    private static readonly int[] SignalFurni = [0, 100, 101, 200, 201];
    private static readonly int[] Users = [0, 10, 11, 200, 201];
    private static readonly int[] MoveUsers = [0, 11, 200, 201];
    private static readonly int[] SignalUsers = [0, 200, 201];
    private static readonly int[] BotCodes = [0, 100, 200, 201];
    // freeze.effect.0..4 localize to fx_218, fx_12, fx_11, fx_53, fx_163. The dropdown stores the index.
    private static readonly int[] FreezeEffects = [218, 12, 11, 53, 163];

    private static readonly Dictionary<string, WiredNativeEditorMetadata> Known = new(StringComparer.Ordinal)
    {
        ["wf_act_toggle_state"] = Meta([Furni], [], [100], [], [0]),
        ["wf_act_toggle_to_rnd"] = Meta([Furni], [], [100], [], []),
        ["wf_act_reset_timers"] = Meta([], [], [], [], []),
        ["wf_act_call_stacks"] = Meta([Furni], [], [100], [], []),
        ["wf_act_neg_call_stacks"] = Meta([Furni], [], [100], [], []),
        ["wf_act_freeze"] = Meta([], [Users], [], [0], [0, 0]),
        ["wf_act_unfreeze"] = Meta([], [Users], [], [0], []),
        ["wf_act_kick_user"] = Meta([], [Users], [], [0], []),
        ["wf_act_mute_triggerer"] = Meta([], [Users], [], [0], [1]),
        ["wf_act_leave_team"] = Meta([], [SignalUsers], [], [0], []),
        ["wf_act_bot_teleport"] = Meta([Furni], [BotCodes], [100], [100], []),
        ["wf_act_bot_move"] = Meta([Furni], [BotCodes], [100], [100], []),
        ["wf_act_bot_follow_avatar"] = Meta([], [Users, BotCodes], [], [0, 100], [0]),
        ["wf_act_bot_give_handitem"] = Meta([], [Users, BotCodes], [], [0, 100], [0]),
        ["wf_act_bot_talk"] = Meta([], [BotCodes], [], [100], [0, -1]),
        ["wf_act_bot_talk_to_avatar"] = Meta([], [Users, BotCodes], [], [0, 100], [0, -1]),
        ["wf_act_bot_clothes"] = Meta([], [BotCodes], [], [100], []),
        ["wf_act_give_reward"] = Meta([], [Users], [], [0], [0, 0, 0, 1]),
        ["wf_act_give_score_tm"] = Meta([], [Users], [], [0], [5, 0, 1]),
        ["wf_act_chase"] = Meta([Furni], [], [100], [], []),
        ["wf_act_flee"] = Meta([Furni], [], [100], [], []),
        ["wf_act_move_rotate_user"] = Meta([], [Users], [], [0], [-1, -1]),
        ["wf_act_rel_mov"] = Meta([Furni], [], [100], [], [0, 0]),
        ["wf_act_furni_to_furni"] = Meta([Furni, FurniAndSnapshot], [], [100, 101], [], []),
        ["wf_act_furni_to_user"] = Meta([Furni], [MoveUsers], [100], [0], []),
        ["wf_act_move_furni_to"] = Meta([Furni, FurniAndSnapshot], [], [100, 101], [], [0, 1]),
        ["wf_act_match_to_sshot"] = Meta([FurniAndSnapshot], [], [100], [], [0, 0, 0, 0], true),
        ["wf_act_user_to_furni"] = Meta([Furni], [MoveUsers], [100], [0], [0]),
        ["wf_act_place_furni"] = Meta([Furni, FurniAndSnapshot, FurniAndSnapshot], [Users, Users], [100, 100, 100], [0, 0], [0, 0, 0, 0, 0, 0, 0, 0, 0, 0], true) with
        {
            VariableDefaults = [WiredVariableAbsent.Id, WiredVariableAbsent.Id]
        },
        ["wf_act_remove_furni"] = Meta([Furni], [], [100], [], []),
        ["wf_act_adjust_clock"] = Meta([Furni], [], [100], [], [0, 0, 0, 0]),
        ["wf_act_neg_send_signal"] = Meta([SignalFurni, SignalFurni], [SignalUsers], [100, 200], [200], [0, 0]),
        ["wf_act_log"] = Meta([], [], [], [], [0]),
        ["wf_act_neg_log"] = Meta([], [], [], [], [0]),
        ["wf_act_click_conf"] = Meta([], [Users], [], [0], [0, 0]),
        ["wf_act_teleport_to_room"] = Meta([Furni], [Users], [100], [0], [])
    };

    internal static bool Supports(string name) => Known.ContainsKey(name);

    internal static WiredNativeEditorMetadata Metadata(string name) =>
        Known.TryGetValue(name, out var metadata) ? metadata : throw new InvalidDataException("This native editor projection is unavailable.");

    /// <summary>Return the derived runtime without an origin; core binds the single Native origin afterwards.</summary>
    internal static bool TryCompile(string name, WiredNativeEditorConfiguration native, out WiredConfiguration runtime)
    {
        runtime = new();

        if (!Supports(name) || native.Delay is not int delay || delay is < 0 or > WiredConfigurationLimits.DelayPulses) {
            return false;
        }

        // Place is the only action here that carries variable ids. Give, remove and change belong to the auxiliary editor.
        if (name != "wf_act_place_furni" && !native.VariableIds.IsEmpty) {
            return false;
        }

        var p = native.OwnedIntParams;
        var text = native.Text;
        int? quota = null;
        WiredTemporaryPlacement? placement = null;
        var furni = ImmutableDictionary.CreateBuilder<string, int>();
        var users = ImmutableDictionary.CreateBuilder<string, int>();
        ImmutableArray<int> parameters;

        switch (name) {
            case "wf_act_toggle_state":
                if (p.Length != 1 || p[0] is < 0 or > 1 || text != "") {
                    return false;
                }

                parameters = [p[0], Source(native, 0)];
                furni["movers"] = Source(native, 0);
                break;
            case "wf_act_toggle_to_rnd":
                if (p.Length != 0 || text != "") {
                    return false;
                }

                parameters = [Source(native, 0)];
                furni["movers"] = Source(native, 0);
                break;
            case "wf_act_reset_timers":
                if (p.Length != 0 || text != "") {
                    return false;
                }

                parameters = [];
                furni["items"] = WiredSources.AllRoom;
                break;
            case "wf_act_call_stacks":
            case "wf_act_neg_call_stacks":
            case "wf_act_chase":
            case "wf_act_flee":
                if (p.Length != 0 || text != "") {
                    return false;
                }

                parameters = [Source(native, 0)];
                furni["items"] = Source(native, 0);
                break;
            case "wf_act_freeze":
                if (p.Length != 2 || p[0] is < 0 or > 4 || p[1] is < 0 or > 1 || text != "") {
                    return false;
                }

                parameters = [FreezeEffects[p[0]], p[1], User(native, 0)];
                users["users"] = User(native, 0);
                break;
            case "wf_act_unfreeze":
            case "wf_act_leave_team":
            case "wf_act_kick_user":
                if (p.Length != 0 || name == "wf_act_kick_user" && text.Length > 100 || name != "wf_act_kick_user" && text != "") {
                    return false;
                }

                parameters = [User(native, 0)];
                users["users"] = User(native, 0);
                break;
            case "wf_act_mute_triggerer":
                // Zero minutes cannot mute: the room deadline rejects a non-positive duration.
                if (p.Length != 1 || p[0] is < 1 or > 10 || text.Length > 100) {
                    return false;
                }

                parameters = [p[0], User(native, 0)];
                users["users"] = User(native, 0);
                break;
            case "wf_act_bot_teleport":
            case "wf_act_bot_move":
                if (p.Length != 0 || !BotName(text)) {
                    return false;
                }

                parameters = [Source(native, 0), User(native, 0)];
                furni["items"] = Source(native, 0);
                users["bots"] = User(native, 0);
                break;
            case "wf_act_bot_follow_avatar":
                if (p.Length != 1 || p[0] is < 0 or > 1 || !BotName(text)) {
                    return false;
                }

                parameters = [p[0], User(native, 0), User(native, 1)];
                users["users"] = User(native, 0);
                users["bots"] = User(native, 1);
                break;
            case "wf_act_bot_give_handitem":
                if (p.Length != 1 || p[0] < 0 || !BotName(text)) {
                    return false;
                }

                parameters = [p[0], User(native, 0), User(native, 1)];
                users["users"] = User(native, 0);
                users["bots"] = User(native, 1);
                break;
            case "wf_act_bot_talk":
                if (p.Length != 2 || p[0] is < 0 or > 1 || p[1] is < -1 or > 2 || !BotName(text)) {
                    return false;
                }

                parameters = [p[0], User(native, 0), p[1]];
                users["bots"] = User(native, 0);
                break;
            case "wf_act_bot_talk_to_avatar":
                if (p.Length != 2 || p[0] is < 0 or > 1 || p[1] is < -1 or > 2 || !BotName(text)) {
                    return false;
                }

                parameters = [p[0], User(native, 0), User(native, 1), p[1]];
                users["users"] = User(native, 0);
                users["bots"] = User(native, 1);
                break;
            case "wf_act_bot_clothes":
                // The save service already checks the figure. The text is stored unchanged.
                if (p.Length != 0 || !BotName(text)) {
                    return false;
                }

                parameters = [User(native, 0)];
                users["bots"] = User(native, 0);
                break;
            case "wf_act_give_reward":
                if (p.Length != 4 || p[0] is < 0 or > 3 || p[1] is < 0 or > 1 || p[2] is < 0 or > 1000 || p[3] is < 1 or > 1000
                    || !WiredRewards.TryEntries(text, out _)) {
                    return false;
                }

                parameters = [p[0], p[1], p[2], p[3], User(native, 0)];
                users["users"] = User(native, 0);
                break;
            case "wf_act_give_score_tm":
                if (p.Length != 3 || p[0] is < -1000 or > 1000 || p[0] == 0 || p[1] is < 0 or > 10 || p[2] is < 1 or > 4 || text != "") {
                    return false;
                }

                parameters = [Math.Abs(p[0]), p[0] < 0 ? 1 : 0, p[2], User(native, 0), p[1]];
                users["users"] = User(native, 0);
                quota = p[1] == 0 ? null : p[1];
                break;
            case "wf_act_move_rotate_user":
                // Client rotation 9 is clockwise and 10 is counter-clockwise. The avatar helper uses 8 and 9.
                if (p.Length != 2 || p[0] is < -1 or > 7 || !TryUserRotation(p[1], out var rotation) || text != "") {
                    return false;
                }

                parameters = [p[0], rotation, User(native, 0)];
                users["users"] = User(native, 0);
                break;
            case "wf_act_rel_mov":
                if (p.Length != 2 || p[0] is < -20 or > 20 || p[1] is < -20 or > 20 || text != "") {
                    return false;
                }

                parameters = [p[0] < 0 ? 0 : 1, Math.Abs(p[0]), p[1] < 0 ? 0 : 1, Math.Abs(p[1]), Source(native, 0)];
                furni["movers"] = Source(native, 0);
                break;
            case "wf_act_furni_to_furni":
                if (p.Length != 0 || text != "") {
                    return false;
                }

                parameters = [Source(native, 0), Source(native, 1)];
                text = ItemList(native.SecondaryItems);
                furni["movers"] = Source(native, 0);
                furni["targets"] = Source(native, 1);
                break;
            case "wf_act_furni_to_user":
                if (p.Length != 0 || text != "") {
                    return false;
                }

                parameters = [Source(native, 0), User(native, 0)];
                furni["movers"] = Source(native, 0);
                users["users"] = User(native, 0);
                break;
            case "wf_act_move_furni_to":
                if (p.Length != 2 || p[0] is not (0 or 2 or 4 or 6) || p[1] is < 1 or > 5 || text != "") {
                    return false;
                }

                parameters = [p[0], p[1], Source(native, 0), Source(native, 1)];
                text = ItemList(native.SecondaryItems);
                furni["movers"] = Source(native, 0);
                furni["targets"] = Source(native, 1);
                break;
            case "wf_act_match_to_sshot":
                if (p.Length != 4 || p.Any(flag => flag is < 0 or > 1) || text != "") {
                    return false;
                }

                parameters = [p[0], p[1], p[2], p[3], Source(native, 0)];
                furni["movers"] = Source(native, 0);
                break;
            case "wf_act_user_to_furni":
                if (p.Length != 1 || p[0] is < 0 or > 2 || text != "") {
                    return false;
                }

                parameters = [Source(native, 0), User(native, 0), p[0]];
                furni["targets"] = Source(native, 0);
                users["users"] = User(native, 0);
                break;
            case "wf_act_place_furni":
                if (p.Length != 10 || text != "" || p[0] is < 0 or > 1 || p[1] is < 0 or > 1 || p[2] is < 0 or > 2
                    || p[3] is < -64 or > 64 || p[4] is < -64 or > 64 || p[5] is < -8000 or > 8000
                    || p[6] is < 0 or > 1 || p[7] is < 0 or > 1 || !TryDomainTarget(p[9], out var valueTarget)
                    || !PlaceVariables(native.VariableIds, (WiredVariableTarget)valueTarget)) {
                    return false;
                }

                placement = new(p[0] == 1, (WiredPlaceLocationType)p[1], (WiredPlaceAltitudeType)p[2], p[3], p[4], p[5],
                    p[6] == 1, p[7] == 1, p[8], valueTarget);
                parameters = [];
                furni["templates"] = Source(native, 0);
                furni["target"] = Source(native, 1);
                furni["value"] = Source(native, 2);
                users["target"] = User(native, 0);
                users["value"] = User(native, 1);
                break;
            case "wf_act_remove_furni":
                if (p.Length != 0 || text != "") {
                    return false;
                }

                parameters = [0, Source(native, 0)];
                furni["items"] = Source(native, 0);
                break;
            case "wf_act_adjust_clock":
                if (p.Length != 4 || p[0] < 0 || p[1] is < 0 or > 99 || p[2] is < 0 or > 1 || p[3] is < 0 or > 2 || text != "") {
                    return false;
                }

                var pulses = p[0] * 2 + p[2];

                if (pulses is < 0 or > 119) {
                    return false;
                }

                parameters = [p[3], Source(native, 0), p[1], pulses];
                furni["items"] = Source(native, 0);
                break;
            case "wf_act_neg_send_signal":
                if (p.Length != 2 || p[0] is < 0 or > 1 || p[1] is < 0 or > 1 || text != "") {
                    return false;
                }

                parameters = [0, Source(native, 1), User(native, 0), p[0], p[1], 0];
                text = ItemList(native.SecondaryItems);
                furni["items"] = Source(native, 0);
                furni["forwarded"] = Source(native, 1);
                users["users"] = User(native, 0);
                break;
            case "wf_act_log":
            case "wf_act_neg_log":
                if (p.Length != 1 || p[0] is < 0 or > 3 || text.Length > 400) {
                    return false;
                }

                parameters = [p[0]];
                break;
            case "wf_act_click_conf":
                if (p.Length != 2 || p[0] is < 0 or > 2 || p[1] is < 0 or > 1 || text != "") {
                    return false;
                }

                parameters = [p[0], p[1], User(native, 0)];
                users["users"] = User(native, 0);
                break;
            case "wf_act_teleport_to_room":
                if (p.Length != 0) {
                    return false;
                }

                if (text.Length > 0) {
                    if (!uint.TryParse(text, out var roomId) || roomId is 0 or > int.MaxValue) {
                        return false;
                    }
                }

                parameters = [User(native, 0), Source(native, 0)];
                users["users"] = User(native, 0);
                furni["links"] = Source(native, 0);
                break;
            default:
                return false;
        }

        runtime = new()
        {
            IntParams = parameters,
            Text = text,
            SelectedItems = native.PrimaryItems.Select(item => item.ItemId).ToImmutableArray(),
            SecondarySelectedItems = native.SecondaryItems.Select(item => item.ItemId).ToImmutableArray(),
            VariableIds = native.VariableIds,
            FurniSources = furni.ToImmutable(),
            UserSources = users.ToImmutable(),
            Delay = delay,
            ScoreQuotaPerGame = quota,
            Snapshots = native.SavedState.Snapshots,
            TemporaryPlacement = placement
        };

        return true;
    }

    // Client merged types: 0 furni, 1 user, -20 context, -10 global. Domain order is user, furni, context, global.
    private static bool TryDomainTarget(int client, out int domain)
    {
        domain = client switch
        {
            1 => (int)WiredVariableTarget.User,
            0 => (int)WiredVariableTarget.Furni,
            -20 => (int)WiredVariableTarget.Context,
            -10 => (int)WiredVariableTarget.Global,
            _ => -1
        };

        return domain >= 0;
    }

    private static bool TryUserRotation(int raw, out int stored)
    {
        stored = raw switch { 9 => 8, 10 => 9, _ => raw };

        return raw is -1 or >= 0 and <= 7 or 9 or 10;
    }

    private static bool BotName(string text) => text.Split('\t', 2)[0].Length <= 64;

    // Spawn is always a furni variable. The operand uses the owned domain target. Ids stay opaque catalog strings.
    private static bool PlaceVariables(ImmutableArray<string> ids, WiredVariableTarget operand) => ids.IsEmpty
        || ids.Length == 2 && PlaceCatalog(ids[0], WiredVariableTarget.Furni) && PlaceCatalog(ids[1], operand);

    private static bool PlaceCatalog(string token, WiredVariableTarget target) => WiredVariableAbsent.Is(token)
        || WiredVariableDescription.TryParseCatalogId(token, out var parsed, out _) && parsed == target;

    private static int Source(WiredNativeEditorConfiguration native, int index) => native.FurniSourceTypes[index];

    private static int User(WiredNativeEditorConfiguration native, int index) => native.UserSourceTypes[index];

    private static string ItemList(ImmutableArray<WiredNativeItemReference> items) => string.Join(';', items.Select(item => item.ItemId));

    private static WiredNativeEditorMetadata Meta(int[][] furni, int[][] users, int[] furniDefaults, int[] userDefaults, int[] owned, bool wall = false) =>
        new(Rows(furni), Rows(users), furniDefaults.ToImmutableArray(), userDefaults.ToImmutableArray(), owned.ToImmutableArray(), wall);

    private static ImmutableArray<ImmutableArray<int>> Rows(int[][] rows) => rows.Select(static row => row.ToImmutableArray()).ToImmutableArray();
}
