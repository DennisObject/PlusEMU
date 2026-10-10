using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Tests;

/// <summary>Reverse of the ordinary 36 action compilers: runtime drafts become native editor records.</summary>
internal static partial class WiredNativeTestSupport
{
    private static readonly int[] FreezeEffects = [218, 12, 11, 53, 163];

    internal static WiredNativeEditorConfiguration? FromActionRuntime(WiredBoxDescriptor descriptor, WiredConfiguration c, Func<uint, bool>? isWall)
    {
        if (!WiredNativeActionEditor.Supports(descriptor.CanonicalName)) {
            return null;
        }

        var native = WiredNativeEditorProjection.DefaultNative(descriptor);
        var name = descriptor.CanonicalName;
        var p = c.IntParams.IsDefault ? ImmutableArray<int>.Empty : c.IntParams;
        int At(int index, int fallback = 0) => index < p.Length ? p[index] : fallback;
        int FurniRole(string role, int fallback) => c.FurniSources.GetValueOrDefault(role, fallback);
        int UserRole(string role, int fallback) => c.UserSources.GetValueOrDefault(role, fallback);
        var owned = native.OwnedIntParams;
        var furni = native.FurniSourceTypes;
        var users = native.UserSourceTypes;
        var secondary = c.SecondarySelectedItems.IsDefault ? ImmutableArray<uint>.Empty : c.SecondarySelectedItems;
        var recoverSecondary = false;
        var secondarySource = -1;

        switch (name) {
            case "wf_act_match_to_sshot":
                // Runtime ints are four flags, then the mover source. The source is not a fifth flag.
                owned = [At(0), At(1), At(2), At(3)];
                furni = [At(4, FurniRole("movers", 100))];
                break;
            case "wf_act_call_stacks":
            case "wf_act_neg_call_stacks":
            case "wf_act_chase":
            case "wf_act_flee":
                // Runtime ints are the single furni source. Owned stays empty.
                owned = [];
                furni = [At(0, FurniRole("items", 100))];
                break;
            case "wf_act_toggle_state":
                owned = [At(0)];
                furni = [At(1, FurniRole("movers", 100))];
                break;
            case "wf_act_toggle_to_rnd":
                owned = [];
                furni = [At(0, FurniRole("movers", 100))];
                break;
            case "wf_act_reset_timers":
                owned = [];
                break;
            case "wf_act_freeze":
                var effect = Array.IndexOf(FreezeEffects, At(0, 218));
                owned = [effect < 0 ? 0 : effect, At(1)];
                users = [At(2, UserRole("users", 0))];
                break;
            case "wf_act_unfreeze":
            case "wf_act_leave_team":
            case "wf_act_kick_user":
                owned = [];
                users = [At(0, UserRole("users", 0))];
                break;
            case "wf_act_mute_triggerer":
                owned = [At(0, 1)];
                users = [At(1, UserRole("users", 0))];
                break;
            case "wf_act_bot_teleport":
            case "wf_act_bot_move":
                owned = [];
                furni = [At(0, FurniRole("items", 100))];
                users = [At(1, UserRole("bots", 100))];
                break;
            case "wf_act_bot_follow_avatar":
            case "wf_act_bot_give_handitem":
                owned = [At(0)];
                users = [At(1, UserRole("users", 0)), At(2, UserRole("bots", 100))];
                break;
            case "wf_act_bot_talk":
                owned = [At(0), At(2, -1)];
                users = [At(1, UserRole("bots", 100))];
                break;
            case "wf_act_bot_talk_to_avatar":
                owned = [At(0), At(3, -1)];
                users = [At(1, UserRole("users", 0)), At(2, UserRole("bots", 100))];
                break;
            case "wf_act_bot_clothes":
                owned = [];
                users = [At(0, UserRole("bots", 100))];
                break;
            case "wf_act_give_reward":
                owned = [At(0), At(1), At(2), At(3, 1)];
                users = [At(4, UserRole("users", 0))];
                break;
            case "wf_act_give_score_tm":
                var quota = p.Length >= 5 ? At(4) : c.ScoreQuotaPerGame ?? 0;
                owned = [At(1) == 1 ? -At(0, 1) : At(0, 1), quota, At(2, 1)];
                users = [p.Length >= 4 ? At(3) : UserRole("users", 0)];
                break;
            case "wf_act_move_rotate_user":
                owned = [At(0, -1), EditorRotation(At(1, -1))];
                users = [At(2, UserRole("users", 0))];
                break;
            case "wf_act_rel_mov":
                owned = [Signed(At(0, 1), At(1)), Signed(At(2, 1), At(3))];
                furni = [At(4, FurniRole("movers", 100))];
                break;
            case "wf_act_furni_to_furni":
                owned = [];
                furni = [At(0, FurniRole("movers", 100)), At(1, FurniRole("targets", 101))];
                recoverSecondary = true;
                secondarySource = 1;
                break;
            case "wf_act_furni_to_user":
                owned = [];
                furni = [At(0, FurniRole("movers", 100))];
                users = [At(1, UserRole("users", 0))];
                break;
            case "wf_act_move_furni_to":
                owned = [At(0), At(1, 1)];
                furni = p.Length >= 4
                    ? [At(2, FurniRole("movers", 100)), At(3, FurniRole("targets", 101))]
                    : [0, At(2, FurniRole("targets", 100))];
                recoverSecondary = p.Length >= 4;
                secondarySource = 1;
                break;
            case "wf_act_user_to_furni":
                owned = [At(2)];
                furni = [At(0, FurniRole("targets", 100))];
                users = [At(1, UserRole("users", 0))];
                break;
            case "wf_act_place_furni":
                if (c.TemporaryPlacement is { } placement) {
                    owned = [
                        placement.TargetIsUser ? 1 : 0, (int)placement.Location, (int)placement.Altitude,
                        placement.OffsetX, placement.OffsetY, placement.OffsetAltitudeHundredths,
                        placement.SpawnWithVariable ? 1 : 0, placement.ValueIsVariable ? 1 : 0,
                        placement.Value, ClientTarget(placement.ValueTarget)
                    ];
                }

                furni = [FurniRole("templates", 100), FurniRole("target", 100), FurniRole("value", 100)];
                users = [UserRole("target", 0), UserRole("value", 0)];
                break;
            case "wf_act_remove_furni":
                owned = [];
                furni = [At(1, FurniRole("items", 100))];
                break;
            case "wf_act_adjust_clock":
                var pulses = At(3);
                owned = [pulses / 2, At(2), pulses % 2, At(0)];
                furni = [At(1, FurniRole("items", 100))];
                break;
            case "wf_act_neg_send_signal":
                owned = [At(3), At(4)];
                furni = [FurniRole("items", 100), At(1, FurniRole("forwarded", 200))];
                users = [At(2, UserRole("users", 200))];
                recoverSecondary = true;
                secondarySource = 1;
                break;
            case "wf_act_log":
            case "wf_act_neg_log":
                owned = [At(0)];
                break;
            case "wf_act_click_conf":
                owned = [At(0), At(1)];
                users = [At(2, UserRole("users", 0))];
                break;
            case "wf_act_teleport_to_room":
                owned = [];
                users = [At(0, UserRole("users", 0))];
                furni = [At(1, FurniRole("links", 100))];
                break;
            default:
                return null;
        }

        if (recoverSecondary && secondary.IsEmpty && ItemIds(c.Text) is { Length: > 0 } parsed) {
            secondary = parsed;

            if (furni[secondarySource] == WiredSources.Selected) {
                furni = furni.SetItem(secondarySource, WiredSources.Snapshot);
            }
        }

        var selected = c.SelectedItems.IsDefault ? ImmutableArray<uint>.Empty : c.SelectedItems;
        WiredNativeItemReference Ref(uint id) => new(id, isWall?.Invoke(id) ?? false);

        return native with
        {
            OwnedIntParams = owned,
            Text = KeepsText(name) ? c.Text ?? "" : "",
            PrimaryItems = [.. selected.Select(Ref)],
            SecondaryItems = [.. secondary.Select(Ref)],
            FurniSourceTypes = furni,
            UserSourceTypes = users,
            VariableIds = name == "wf_act_place_furni" && !c.VariableIds.IsDefault && !c.VariableIds.IsEmpty
                ? c.VariableIds
                : native.VariableIds,
            Delay = c.Delay,
            SavedState = new() { Snapshots = c.Snapshots.IsDefault ? [] : c.Snapshots }
        };
    }

    private static bool KeepsText(string name) => name is "wf_act_kick_user" or "wf_act_mute_triggerer"
        or "wf_act_bot_teleport" or "wf_act_bot_move" or "wf_act_bot_follow_avatar" or "wf_act_bot_give_handitem"
        or "wf_act_bot_talk" or "wf_act_bot_talk_to_avatar" or "wf_act_bot_clothes" or "wf_act_give_reward"
        or "wf_act_log" or "wf_act_neg_log" or "wf_act_teleport_to_room";

    // Stored 8 and 9 are the avatar helper's clockwise and counter-clockwise codes. The editor stores 9 and 10.
    private static int EditorRotation(int stored) => stored switch { 8 => 9, 9 => 10, _ => stored };

    private static int Signed(int sign, int magnitude) => sign == 0 ? -magnitude : magnitude;

    // Domain order is user, furni, context, global. The editor stores 1, 0, -20, -10.
    private static int ClientTarget(int domain) => domain switch { 0 => 1, 1 => 0, 2 => -20, 3 => -10, _ => 0 };

    private static ImmutableArray<uint> ItemIds(string text)
    {
        if (string.IsNullOrEmpty(text)) {
            return [];
        }

        var ids = ImmutableArray.CreateBuilder<uint>();

        foreach (var field in text.Split([';', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (!uint.TryParse(field, out var id) || id == 0) {
                return [];
            }

            ids.Add(id);
        }

        return ids.ToImmutable();
    }
}
