using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;

namespace Plus.Tests;

internal static partial class WiredNativeTestSupport
{
    internal static WiredNativeEditorConfiguration? FromOtherRuntime(WiredBoxDescriptor descriptor, WiredConfiguration c, Func<uint, bool>? isWall)
    {
        if (descriptor.Category == WiredBoxCategory.Action)
            return FromActionRuntime(descriptor, c, isWall);

        var n = WiredNativeEditorProjection.DefaultNative(descriptor) with
        {
            Text = c.Text,
            PrimaryItems = [.. c.SelectedItems.Select(id => new WiredNativeItemReference(id, isWall?.Invoke(id) ?? false))],
            SecondaryItems = [.. c.SecondarySelectedItems.Select(id => new WiredNativeItemReference(id, isWall?.Invoke(id) ?? false))],
            SavedState = new() { Snapshots = c.Snapshots }
        };
        var p = c.IntParams;
        int P(int i, int fallback = 0) => i < p.Length ? p[i] : fallback;
        var owned = n.OwnedIntParams;
        var f = n.FurniSourceTypes;
        var u = n.UserSourceTypes;
        var q = 0;
        var name = WiredConditionConfiguration.NegativeNames.GetValueOrDefault(descriptor.CanonicalName, descriptor.CanonicalName);

        if (descriptor.Category == WiredBoxCategory.Trigger) {
            switch (name) {
                case "wf_trg_enter_room" or "wf_trg_leave_room" or "wf_trg_game_starts" or "wf_trg_game_ends" or "wf_trg_collision":
                    owned = [];
                    break;
                case "wf_trg_walks_off_furni" or "wf_trg_click_furni" or "wf_trg_click_tile":
                    owned = [];
                    f = [P(0, 100)];
                    break;
                case "wf_trg_state_changed":
                    owned = [P(0)];
                    f = [P(1, 100)];
                    break;
                case "wf_trg_clock_counter":
                    owned = [P(1) / 2, P(0), P(1) % 2];
                    f = [P(2, 100)];
                    break;
                case "wf_trg_bot_reached_stf":
                    owned = [];
                    f = [P(0, 100)];
                    u = [P(1)];
                    break;
                case "wf_trg_bot_reached_avtr":
                    owned = [];
                    u = [P(0)];
                    break;
                case "wf_trg_click_user" or "wf_trg_score_achieved":
                    owned = [P(0), P(1)];
                    break;
                case "wf_trg_user_performs_action":
                    owned = [AvatarCode(P(0))];
                    n = n with { Text = AvatarText(p) };
                    break;
                default:
                    owned = p;
                    break;
            }
        }
        else if (descriptor.Category == WiredBoxCategory.Condition) {
            switch (name) {
                case "wf_cnd_actor_dir" or "wf_cnd_actor_in_team" or "wf_cnd_has_handitem" or "wf_cnd_wearing_effect":
                    owned = [P(0)];
                    u = [P(1)];
                    q = P(2);
                    break;
                case "wf_cnd_actor_in_group":
                    owned = [];
                    u = [P(0)];
                    q = P(3);
                    n = n with { Text = P(1) == 0 ? "" : P(2).ToString(CultureInfo.InvariantCulture) };
                    break;
                case "wf_cnd_wearing_badge":
                    owned = [];
                    u = [P(0)];
                    q = P(1);
                    break;
                case "wf_cnd_user_performs_action":
                    owned = [AvatarCode(P(0))];
                    u = [P(5)];
                    q = P(6);
                    n = n with { Text = AvatarText(p) };
                    break;
                case "wf_cnd_triggerer_match":
                    owned = [P(0)];
                    u = [P(2), P(3)];
                    q = P(4);
                    break;
                case "wf_cnd_trggrer_on_frn":
                    owned = [];
                    f = [P(0, 100)];
                    u = [P(1)];
                    q = P(2);
                    break;
                case "wf_cnd_has_furni_on":
                    owned = [P(0)];
                    f = [P(1, 100)];
                    break;
                case "wf_cnd_match_snapshot":
                    owned = [P(0), P(1), P(2), P(3)];
                    f = [P(4, 100)];
                    q = P(5);
                    break;
                case "wf_cnd_stuff_is":
                    owned = [];
                    f = [c.FurniSources.GetValueOrDefault("items", P(0, 100)), c.FurniSources.GetValueOrDefault("comparison", P(1, 101))];
                    q = P(2);
                    n = n with { Text = "" };
                    break;
                case "wf_cnd_has_altitude":
                    owned = [(int)(decimal.Parse(c.Text, CultureInfo.InvariantCulture) * 100), P(0)];
                    f = [P(1, 100)];
                    q = P(2);
                    n = n with { Text = "" };
                    break;
                case "wf_cnd_valid_moves":
                    owned = [];
                    f = [c.FurniSources.GetValueOrDefault("items", 100)];
                    break;
                case "wf_cnd_slc_quantity":
                    owned = [P(2) == 1 ? 0 : 1, P(1), P(0)];

                    if (P(2) == 1)
                        f = [P(3)];
                    else
                        u = [P(3)];

                    break;
                case "wf_cnd_team_has_rank":
                    owned = [P(0), P(1)];
                    u = [P(2)];
                    q = P(3);
                    break;
                case "wf_cnd_team_has_score":
                    owned = [P(0), P(2), P(1)];
                    u = [P(3)];
                    q = P(4);
                    break;
                case "wf_cnd_counter_time_matches":
                    owned = [P(2) / 2, P(1), P(2) % 2, P(0)];
                    f = [P(3, 100)];
                    q = P(4);
                    break;
                case "wf_cnd_match_time":
                    owned = [P(6) / 2, P(3) / 2, P(0) / 2, P(7), P(8), P(4), P(5), P(1), P(2)];
                    break;
                case "wf_cnd_match_date":
                    owned = [P(1) / 2, P(5) / 2, P(0), P(2), P(3), P(4), P(6), P(7)];
                    break;
                default:
                    owned = p;
                    break;
            }

            n = n with { Quantifier = q };
        }
        else if (descriptor.Category == WiredBoxCategory.Selector) {
            var filter = P(Math.Max(0, p.Length - 2)) != 0;
            var inverse = P(Math.Max(0, p.Length - 1)) != 0;

            switch (name) {
                case "wf_slc_furni_bytype":
                    owned = [P(1)];
                    f = [c.FurniSources.GetValueOrDefault("items", 100)];
                    break;
                case "wf_slc_users_bytype" or "wf_slc_users_team" or "wf_slc_users_handitem":
                    owned = [P(0)];
                    break;
                case "wf_slc_furni_onfurni":
                    owned = [P(0)];
                    f = [P(1, 100)];
                    break;
                case "wf_slc_users_onfurni":
                    owned = [];
                    f = [P(0, 100)];
                    break;
                case "wf_slc_users_group":
                    owned = [];
                    n = n with { Text = P(0) == 0 ? "" : P(1).ToString(CultureInfo.InvariantCulture) };
                    break;
                case "wf_slc_users_byaction":
                    owned = [AvatarCode(P(0))];
                    n = n with { Text = AvatarText(p) };
                    break;
                case "wf_slc_furni_area" or "wf_slc_users_area":
                    owned = [P(0), P(1), P(2), P(3)];
                    break;
                case "wf_slc_furni_altitude":
                    owned = [(int)(decimal.Parse(c.Text, CultureInfo.InvariantCulture) * 100), P(0)];
                    n = n with { Text = "" };
                    break;
                case "wf_slc_remote":
                    filter = P(0) != 0;
                    inverse = P(1) != 0;
                    owned = [P(2), P(3)];
                    f = [P(4, 100)];
                    break;
                case "wf_slc_furni_neighborhood" or "wf_slc_users_neighborhood":
                    owned = p;
                    f = [c.FurniSources.GetValueOrDefault("anchor", 100)];
                    u = [c.UserSources.GetValueOrDefault("anchor", 0)];
                    filter = false;
                    inverse = false;
                    break;
                default:
                    owned = [];
                    break;
            }

            n = n with { Filter = filter, Inverse = inverse };
        }
        else if (descriptor.Category == WiredBoxCategory.Addon) {
            switch (name) {
                case "wf_xtra_mov_curve":
                    owned = [P(3), P(2, 80), WiredNativeAuxiliaryEditor.AirTarget(P(4))];
                    f = [P(6)];
                    u = [P(5)];
                    n = n with { Text = "", VariableIds = c.VariableIds.IsEmpty ? [CatalogId(c.Text, P(4))] : c.VariableIds };
                    break;
                case "wf_xtra_mov_physics":
                    owned = [P(0), P(1), P(2), P(3)];
                    f = [P(4), P(5)];
                    u = [P(6)];
                    break;
                case "wf_xtra_mov_carry_users":
                    owned = [P(0)];
                    u = [P(1)];
                    break;
                case "wf_xtra_execution_limit":
                    owned = [P(0, 1), P(1, 500) > 20 ? P(1) / 500 : P(1, 1)];
                    break;
                case "wf_xtra_random":
                    owned = [P(1), P(0, 1)];
                    break;
                case "wf_xtra_or_eval":
                    owned = P(0) > 3 ? [-1, P(0) - 4, P(2)] : [P(0), 0, 0];
                    break;
                case "wf_xtra_filter_furni" or "wf_xtra_filter_users":
                    owned = [P(0, 1), P(1), WiredNativeAuxiliaryEditor.AirTarget(P(2, name == "wf_xtra_filter_users" ? 0 : 1))];
                    u = [P(3)];
                    f = [P(4)];
                    break;
                case "wf_xtra_text_output_furni_name":
                    owned = [P(0) == 2 ? 1 : 0];
                    f = [P(1)];
                    break;
                case "wf_xtra_text_output_username":
                    owned = [P(0) == 2 ? 1 : 0];
                    u = [P(1)];
                    break;
                default:
                    owned = p;
                    break;
            }
        }
        else
            return null;

        return n with { OwnedIntParams = owned, FurniSourceTypes = f, UserSourceTypes = u };
    }

    private static int AvatarCode(int action) => action switch { 1 => 0, 2 => 1, 3 => 2, 12 => 3, 9 => 10, 10 => 11, 11 => 67, _ => action };
    private static string AvatarText(ImmutableArray<int> p) => p[0] == 9 && p.Length > 2 && p[1] != 0 ? p[2].ToString(CultureInfo.InvariantCulture)
        : p[0] == 10 && p.Length > 4 && p[3] != 0 ? "dance " + p[4] : "";

    public static bool TryCompileRuntime(IWiredConfiguredItem box, WiredConfiguration draft, out WiredConfiguration runtime) =>
        WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor, FromRuntime(box.Descriptor, draft, id => box.Instance.GetRoomItemHandler()?.GetItem(id)?.IsWallItem ?? false), out runtime);

    public static void InstallRuntime(IWiredConfiguredItem box, WiredConfiguration draft) => Install(box,
        FromRuntime(box.Descriptor, draft, id => box.Instance.GetRoomItemHandler()?.GetItem(id)?.IsWallItem ?? false));
    public static bool TryValidateRuntime(IWiredConfiguredItem box, WiredConfiguration draft, out WiredConfiguration validated, out string error)
    {
        if (!WiredNativeEditorProjection.Supports(box.Descriptor.CanonicalName) || WiredNativeEditorProjection.IsBound(box.Item.Id, box.Descriptor, draft))
            return box.TryValidateConfiguration(draft, out validated, out error);

        validated = draft;
        error = "Invalid native fixture configuration.";

        try {
            return TryCompileRuntime(box, draft, out var compiled) && box.TryValidateConfiguration(compiled, out validated, out error);
        }
        catch (Exception exception) when (exception is ArgumentException or IndexOutOfRangeException or InvalidOperationException) {
            error = exception.Message;

            return false;
        }
    }
}
