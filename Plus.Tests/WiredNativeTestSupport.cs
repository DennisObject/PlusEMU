using Xunit;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.Tests;

/// <summary>Test-only derivation of a native editor record from a runtime-shaped draft of the mapped cards.</summary>
internal static partial class WiredNativeTestSupport
{
    private static readonly int[] MoveRotateMovement = [-1, 8, 9, 10, 0, 2, 4, 6, 1, 3, 5, 7];
    private static readonly int[] MoveRotateTurn = [0, 2, 4, 6];

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
                // The editor lists movements and turns by option index; the stored runtime holds the direction/turn values.
                owned = p.Length == 3 ? [p[0], p[1]] : [Array.IndexOf(MoveRotateMovement, p[0]), Array.IndexOf(MoveRotateTurn, p[1])];
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
                owned = [p[1] == 1 ? -p[0] : p[0], configuration.ScoreQuotaPerGame ?? (p.Length > 3 ? p[3] : 0)];
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
                owned = p.Length == 2 ? [p[0], 1, 0] : [p[0], p[1], p[2]];
                furni = p.Length == 2 ? [p[1], 101] : [p[3], p[4] == 100 ? 101 : p[4]];
                users = p.Length == 2 ? [0] : [p[5]];
                break;
            default:
                return FromOtherRuntime(descriptor, configuration, isWall)
                    ?? throw new InvalidOperationException("No test mapping for " + descriptor.CanonicalName);
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
            Quantifier = descriptor.Category == WiredBoxCategory.Condition ? 0 : null,
            SavedState = new() { Snapshots = configuration.Snapshots }
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
    public static bool TrySavePrepared(IWiredConfiguredItem box, WiredConfiguration proposed, IWiredConfigurationStore store, out string error)
    {
        var prepared = Plus.HabboHotel.Items.Wired.Modern.WiredRoomOperations.PrepareSnapshots(box, proposed);
        error = "Invalid native fixture configuration.";

        return TryCompileRuntime(box, prepared, out var compiled)
            && WiredConfigurationSave.TrySave(box, compiled, store, out error);
    }

    /// <summary>
    /// A chest-family native record from the old fixture shape: scalar target codes at <paramref name="targetSlots"/>, per-selection
    /// sources, the two pick groups (source 100 and 101) and catalog variable ids.
    /// </summary>
    public static WiredNativeEditorConfiguration Chest(string name, int[] scalarInts, int[] targetSlots, int[] furniSources,
        int[] userSources, string[] variables, uint[] primary, uint[] secondary, string text = "")
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));
        var owned = (int[])scalarInts.Clone();

        foreach (var slot in targetSlots) {
            owned[slot] = WiredNativeAuxiliaryEditor.AirTarget(owned[slot]);
        }

        return WiredNativeEditorProjection.DefaultNative(descriptor) with
        {
            OwnedIntParams = [.. owned],
            FurniSourceTypes = [.. furniSources],
            UserSourceTypes = [.. userSources],
            VariableIds = [.. variables],
            PrimaryItems = [.. primary.Select(id => new WiredNativeItemReference(id, false))],
            SecondaryItems = [.. secondary.Select(id => new WiredNativeItemReference(id, false))],
            Text = text
        };
    }

    /// <summary>Compile, validate through the box and publish: the native save path without a session.</summary>
    public static void Install(IWiredConfiguredItem box, WiredNativeEditorConfiguration native)
    {
        Assert.True(WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor, native, out var runtime));
        Assert.True(box.TryValidateConfiguration(runtime, out var validated, out var error), error);
        box.ApplyConfiguration(validated);
    }

    /// <summary>
    /// Fixture shim for scalar executors: the old drafts name variables by token in the text; the native shape names them by catalog id.
    /// The target of each slot picks the id prefix (0 user, 1 furni, 2 context, 3 room).
    /// </summary>
    public static WiredConfiguration Scalar(string name, WiredConfiguration legacy)
    {
        var tokens = legacy.Text.Split('\t');
        var p = legacy.IntParams;
        string Id(string token, int target)
        {
            if (token.Length == 0) {
                return "n";
            }

            var prefix = target switch { 0 => "user:", 1 => "furni:", 2 => "ctx:", _ => "room:" };

            return prefix + (token.StartsWith("custom:", StringComparison.Ordinal) ? token[7..] : token);
        }

        var operand = name is "wf_act_change_var_val" or "wf_cnd_var_val_match";
        var ids = operand ? new[] { Id(tokens[0], p[0]), Id(tokens.Length > 1 ? tokens[1] : "", p.Length > 4 ? p[4] : 0) } : [Id(tokens[0], p[0])];

        return legacy with { Text = "", VariableIds = [.. ids] };
    }

    /// <summary>
    /// Variable-family fixtures written as the old runtime drafts, as native records: definitions, text connector, time utility, level
    /// system. Echo and reference drafts name their source by the old JSON/token; the native form names it by catalog id.
    /// </summary>
    public static WiredNativeEditorConfiguration FromLegacyVariableDraft(WiredBoxDescriptor descriptor, WiredConfiguration draft)
    {
        var defaults = WiredNativeEditorProjection.DefaultNative(descriptor);
        var p = draft.IntParams;

        switch (descriptor.CanonicalName) {
            case "wf_var_user":
                return defaults with { OwnedIntParams = [p[1], p[0]], Text = draft.Text };
            case "wf_var_furni":
                return defaults with { OwnedIntParams = [p[0], p[1]], Text = draft.Text };
            case "wf_var_room":
                return defaults with { OwnedIntParams = [p[0]], Text = draft.Text };
            case "wf_var_context":
                return defaults with { OwnedIntParams = [p[0]], Text = draft.Text };
            case "wf_var_echo" or "wf_var_reference":
                using (var document = JsonDocument.Parse(draft.Text)) {
                    var root = document.RootElement;
                    var name = root.GetProperty("variableName").GetString() ?? "";
                    var target = root.GetProperty("sourceTargetType").GetInt32();
                    var prefix = target switch { 0 => "user:", 1 => "furni:", 2 => "ctx:", _ => "room:" };

                    if (descriptor.CanonicalName == "wf_var_echo") {
                        var token = root.GetProperty("sourceVariableToken").GetString() ?? "";

                        return defaults with { Text = name, VariableIds = [prefix + (token.StartsWith("custom:", StringComparison.Ordinal) ? token[7..] : token)] };
                    }

                    return defaults with
                    {
                        Text = name,
                        OwnedIntParams = [root.TryGetProperty("readOnly", out var readOnly) && !readOnly.GetBoolean() ? 0 : 1],
                        VariableIds = [$"shared:{root.GetProperty("sourceRoomId").GetUInt32()}:{(target == 3 ? "room" : "user")}:{root.GetProperty("sourceVariableItemId").GetUInt32()}"]
                    };
                }
            case "wf_var_quest" or "wf_var_quest_chain" or "wf_xtra_var_text_connector":
                return defaults with { Text = draft.Text };
            case "wf_xtra_var_time_util":
                return defaults with { OwnedIntParams = [.. p] };
            case "wf_xtra_var_lvlup_system":
                using (var document = JsonDocument.Parse(draft.Text)) {
                    var root = document.RootElement;
                    int Number(string key, int fallback) => root.TryGetProperty(key, out var value) ? value.GetInt32() : fallback;
                    var mask = root.TryGetProperty("subvariables", out var subvariables) ? subvariables.EnumerateArray().Sum(item => 1 << item.GetInt32()) : 3;

                    return Number("mode", 1) switch
                    {
                        1 => defaults with { OwnedIntParams = [mask, 1, Number("stepSize", 100), Number("maxLevel", 10)] },
                        2 => defaults with { OwnedIntParams = [mask, 2, Number("firstLevelXp", 100), Number("increaseFactor", 100), Number("maxLevel", 10)] },
                        _ => defaults with { OwnedIntParams = [mask, 0], Text = root.GetProperty("interpolationText").GetString() ?? "" }
                    };
                }
            default:
                throw new InvalidOperationException("No legacy variable draft mapping for " + descriptor.CanonicalName);
        }
    }

    public static void InstallLegacyVariableDraft(IWiredConfiguredItem box, WiredConfiguration draft)
    {
        // A name without a native mapping keeps its own runtime validator.
        if (!WiredNativeEditorProjection.Supports(box.Descriptor.CanonicalName)) {
            Assert.True(box.TryValidateConfiguration(draft, out var validated, out var error), error);
            box.ApplyConfiguration(validated);

            return;
        }

        Install(box, FromLegacyVariableDraft(box.Descriptor, draft));
    }

    private static string CatalogId(string token, int scalarTarget)
    {
        if (token.Length == 0) {
            return "n";
        }

        var prefix = scalarTarget switch { 0 => "user:", 1 => "furni:", 2 => "ctx:", _ => "room:" };

        return prefix + (token.StartsWith("custom:", StringComparison.Ordinal) ? token[7..] : token);
    }

    /// <summary>The old variable-addon drafts (filter by variable, variable text output) as native records.</summary>
    public static WiredNativeEditorConfiguration FromLegacyVariableAddon(WiredBoxDescriptor descriptor, WiredConfiguration draft)
    {
        var defaults = WiredNativeEditorProjection.DefaultNative(descriptor);
        var p = draft.IntParams;
        var tokens = draft.Text.Split('\t');

        if (descriptor.CanonicalName == "wf_xtra_text_output_variable") {
            var name = tokens[1] + (tokens.Length > 2 && tokens[2].Length > 0 ? "\t" + tokens[2] : "");

            return defaults with
            {
                OwnedIntParams = [p[2] == 2 ? 1 : 0, WiredNativeAuxiliaryEditor.AirTarget(p[0]), p[1] == 2 ? 1 : 0],
                UserSourceTypes = [p[3]],
                FurniSourceTypes = [p[4]],
                VariableIds = [CatalogId(tokens[0], p[0])],
                Text = name
            };
        }

        var filtered = descriptor.CanonicalName == "wf_xtra_filter_users_by_var" ? 0 : 1;

        return defaults with
        {
            OwnedIntParams = [p[2], p[0], p[1], WiredNativeAuxiliaryEditor.AirTarget(p[3])],
            UserSourceTypes = [p[4]],
            FurniSourceTypes = [p[5]],
            VariableIds = [CatalogId(tokens[0], filtered), CatalogId(tokens.Length > 1 ? tokens[1] : "", p[3])]
        };
    }
}
