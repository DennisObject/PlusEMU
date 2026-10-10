using Xunit;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Tests;

/// <summary>Test-only derivation of a native editor record from a runtime-shaped draft of the mapped cards.</summary>
internal static class WiredNativeTestSupport
{
    public static WiredNativeEditorConfiguration FromRuntime(WiredBoxDescriptor descriptor, WiredConfiguration configuration,
        Func<uint, bool>? isWall = null)
    {
        var p = configuration.IntParams;
        ImmutableArray<int> owned = [], furni = [], users = [];
        var text = "";

        switch (descriptor.CanonicalName) {
            case "wf_trg_says_something":
                owned = [p[2], p[0], p[1]];
                text = configuration.Text;
                break;
            case "wf_trg_periodically" or "wf_trg_period_short" or "wf_trg_period_long" or "wf_trg_at_given_time":
                owned = [p[0]];
                break;
            case "wf_trg_recv_signal" or "wf_trg_stuff_state":
                furni = [p[1]];
                break;
            case "wf_cnd_user_count_in":
                owned = [p[0], p[1]];
                break;
            case "wf_act_teleport_to":
                owned = [p[0]];
                furni = [p[1]];
                users = [p[2]];
                break;
            case "wf_trg_walks_on_furni":
                owned = [];
                furni = [p[0]];
                break;
            case "wf_act_show_message":
                owned = [p[1], p[2], p.Length == 4 ? p[3] : -1];
                users = [p[0]];
                text = configuration.Text;
                break;
            case "wf_cnd_furnis_hv_avtrs":
                owned = [p[0]];
                furni = [p[1]];
                break;
            case "wf_act_move_rotate":
                var editor = Plus.HabboHotel.Items.Wired.Modern.Actions.WiredMovementConfiguration.ForEditor(descriptor.CanonicalName, configuration).IntParams;
                owned = [editor[0], editor[1]];
                furni = [p[2]];
                break;
            case "wf_act_move_to_dir":
                owned = [p[0], p[1], p[3]];
                furni = [p[2]];
                break;
            case "wf_act_control_clock":
                owned = [p[0]];
                furni = [p[1]];
                break;
            case "wf_act_join_team":
                owned = [p[1], p[0]];
                users = [p[2]];
                break;
            case "wf_act_give_score":
                owned = [p[1] == 1 ? -p[0] : p[0], configuration.ScoreQuotaPerGame ?? 0];
                users = [p[2]];
                break;
            case "wf_act_set_altitude":
                owned = [(int)(decimal.Parse(configuration.Text, CultureInfo.InvariantCulture) * 100m), p[0]];
                furni = [p[1]];
                break;
            case "wf_act_send_signal":
                owned = [p[3], p[4]];
                furni = [100, p[1] == 100 ? 101 : p[1]];
                users = [p[2]];
                break;
            case "wf_act_move_furni_as_group":
                owned = [p[0], p[1], p[2]];
                furni = [p[3], p[4] == 100 ? 101 : p[4]];
                users = [p[5]];
                break;
            default:
                throw new InvalidOperationException("No test mapping for " + descriptor.CanonicalName);
        }

        WiredNativeItemReference Ref(uint id) => new(id, isWall?.Invoke(id) ?? false);

        return new()
        {
            Category = descriptor.Category,
            NativeCode = descriptor.EditorCode,
            OwnedIntParams = owned,
            Text = text,
            PrimaryItems = [.. configuration.SelectedItems.Select(Ref)],
            SecondaryItems = [.. configuration.SecondarySelectedItems.Select(Ref)],
            FurniSourceTypes = furni,
            UserSourceTypes = users,
            Delay = descriptor.Category == WiredBoxCategory.Action ? configuration.Delay : null,
            Quantifier = descriptor.Category == WiredBoxCategory.Condition ? 0 : null
        };
    }

    /// <summary>A schema 2 row for the real store, built from a runtime-shaped JSON draft.</summary>
    public static ModernWiredRuntimeTests.StoredRuntimeRow NativeRow(uint itemId, string name, string runtimeJson)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        var native = FromRuntime(descriptor, JsonSerializer.Deserialize<WiredConfiguration>(runtimeJson)!);

        return new(itemId, name, 2, JsonSerializer.Serialize(native));
    }

    /// <summary>Snapshot-box saves capture picks before pure validation; mapped cards do this in the service instead.</summary>
    public static bool TrySavePrepared(IWiredConfiguredItem box, WiredConfiguration proposed, IWiredConfigurationStore store, out string error) =>
        WiredConfigurationSave.TrySave(box, Plus.HabboHotel.Items.Wired.Modern.WiredRoomOperations.PrepareSnapshots(box, proposed), store, out error);
}
