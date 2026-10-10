using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Modern.Actions;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Native records own editor data; runtime fields are checked immutable projections.</summary>
public static class WiredNativeEditorProjection
{
    private static readonly int[] ShowStyles = [34, 200, 201, 202, 210, 211, 212, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 250, 251, 252];

    private static readonly HashSet<string> BuiltIn = ["wf_trg_says_something", "wf_trg_walks_on_furni", "wf_trg_periodically",
        "wf_cnd_user_count_in", "wf_act_teleport_to", "wf_act_show_message", "wf_cnd_furnis_hv_avtrs", "wf_act_move_rotate",
        "wf_act_move_to_dir", "wf_act_control_clock", "wf_act_join_team", "wf_act_give_score", "wf_act_set_altitude",
        "wf_act_send_signal", "wf_act_move_furni_as_group"];

    // The auxiliary editor is asked first for every category (variable/chest triggers, conditions and effects live there);
    // the ordinary per-category helpers handle the rest. A name belongs to exactly one helper.
    public static bool Supports(string name) => BuiltIn.Contains(name) || WiredNativeAuxiliaryEditor.Supports(name)
        || WiredBoxRegistry.TryGet(name, out var descriptor) && descriptor.Category switch
        {
            WiredBoxCategory.Trigger => WiredNativeTriggerEditor.Supports(name),
            WiredBoxCategory.Action => WiredNativeActionEditor.Supports(name),
            WiredBoxCategory.Condition => WiredNativeConditionEditor.Supports(name),
            WiredBoxCategory.Selector => WiredNativeSelectorEditor.Supports(name),
            _ => false
        };

    /// <summary>The registry editor code; code 0 is a valid dialog (says-something, toggle-state).</summary>
    public static int Code(string name) => WiredBoxRegistry.TryGet(name, out var descriptor) ? descriptor.EditorCode
        : throw new InvalidDataException("This native editor projection is unavailable.");

    public static WiredNativeEditorMetadata Metadata(string name) => name switch
    {
        // Says borrows the Sept9 reset defaults; remaining footer fields advertise local support.
        // No Sept16 server metadata oracle is claimed.
        "wf_trg_says_something" => new([], [], [], [], [0, 0, 1], false),
        // Walk/Show footer defaults are local support; Condition borrows Sept9, not Sept16 metadata.
        "wf_trg_walks_on_furni" => new([[0, 100, 200, 201]], [], [100], [], [], false),
        "wf_act_show_message" => new([], [[0, 11, 200, 201]], [], [0], [0, 34, -1], false),
        "wf_cnd_furnis_hv_avtrs" => new([[0, 100, 200, 201]], [], [100], [], [1], false),
        // Complete Sept9 server footers are borrowed; no official Sept16/v86 metadata claim.
        "wf_trg_periodically" => new([], [], [], [], [1], false),
        "wf_cnd_user_count_in" => new([], [], [], [], [1, 50], false),
        "wf_act_teleport_to" => new([[0, 100, 200, 201]], [[0, 200, 201]], [100], [0], [0], false),
        // Rotate's two owned fields/defaults and floor-only scope are the explicit local subset.
        // The captured server third field and wall support have no proven executable mapping here.
        "wf_act_move_rotate" => new([[0, 100, 200, 201]], [], [100], [], [0, 0], false),
        "wf_act_move_to_dir" => new([[0, 100, 200, 201]], [], [100], [], [0, 0, 1], false),
        "wf_act_control_clock" => new([[0, 100, 200, 201]], [], [100], [], [0], false),
        "wf_act_join_team" => new([], [[0, 200, 201]], [], [0], [1, 0], false),
        "wf_act_give_score" => new([], [[0, 200, 201]], [], [0], [5, 0], false),
        "wf_act_set_altitude" => new([[0, 100, 200, 201]], [], [100], [], [0, 0], true),
        "wf_act_send_signal" => new([[0, 100, 101, 200, 201], [0, 100, 101, 200, 201]], [[0, 200, 201]], [100, 200], [200], [0, 0], false),
        // This is the reviewed executable local subset, not an official Group metadata oracle.
        "wf_act_move_furni_as_group" => new([[0, 100, 200, 201], [0, 100, 101, 200, 201]], [[0, 200, 201]], [100, 101], [0], [0, 0, 0], false),
        _ => CategoryMetadata(name)
    };

    private static WiredNativeEditorMetadata CategoryMetadata(string name) => WiredNativeAuxiliaryEditor.Supports(name)
        ? WiredNativeAuxiliaryEditor.Metadata(name)
        : WiredBoxRegistry.TryGet(name, out var descriptor)
            ? descriptor.Category switch
            {
                WiredBoxCategory.Trigger => WiredNativeTriggerEditor.Metadata(name),
                WiredBoxCategory.Action => WiredNativeActionEditor.Metadata(name),
                WiredBoxCategory.Condition => WiredNativeConditionEditor.Metadata(name),
                WiredBoxCategory.Selector => WiredNativeSelectorEditor.Metadata(name),
                _ => throw new InvalidDataException("This native editor projection is unavailable.")
            }
            : throw new InvalidDataException("This native editor projection is unavailable.");

    private static bool TryCompileCategory(WiredBoxDescriptor descriptor, WiredNativeEditorConfiguration native,
        out WiredConfiguration runtime) => WiredNativeAuxiliaryEditor.Supports(descriptor.CanonicalName)
        ? WiredNativeAuxiliaryEditor.TryCompile(descriptor.CanonicalName, native, out runtime)
        : descriptor.Category switch
        {
            WiredBoxCategory.Trigger => WiredNativeTriggerEditor.TryCompile(descriptor.CanonicalName, native, out runtime),
            WiredBoxCategory.Action => WiredNativeActionEditor.TryCompile(descriptor.CanonicalName, native, out runtime),
            WiredBoxCategory.Condition => WiredNativeConditionEditor.TryCompile(descriptor.CanonicalName, native, out runtime),
            WiredBoxCategory.Selector => WiredNativeSelectorEditor.TryCompile(descriptor.CanonicalName, native, out runtime),
            _ => Fail(out runtime)
        };

    private static bool Fail(out WiredConfiguration runtime)
    {
        runtime = new();

        return false;
    }

    public static bool TryCompile(uint itemId, WiredBoxDescriptor descriptor, WiredNativeEditorConfiguration native,
        out WiredConfiguration runtime)
    {
        runtime = new();
        var name = descriptor.CanonicalName;

        if (itemId == 0 || !Supports(name) || native.Version != 2 || native.Category != descriptor.Category
            || native.NativeCode != Code(name) || !WithinBounds(native)) {
            return false;
        }

        var metadata = Metadata(name);

        if (native.FurniSourceTypes.Length != metadata.FurniAllowed.Length
            || native.UserSourceTypes.Length != metadata.UsersAllowed.Length
            || native.FurniSourceTypes.Where((source, index) => !metadata.FurniAllowed[index].Contains(source)).Any()
            || native.UserSourceTypes.Where((source, index) => !metadata.UsersAllowed[index].Contains(source)).Any()
            || !metadata.AllowWall && native.PrimaryItems.Concat(native.SecondaryItems).Any(item => item.Wall)
            || BuiltIn.Contains(name) && native.VariableIds.Length != 0) {
            return false;
        }

        if (!BuiltIn.Contains(name)) {
            if (!TryCompileCategory(descriptor, native, out var derivedCategory) || !WiredLegacyProtocol.IsWithinLimits(derivedCategory)) {
                return false;
            }

            runtime = derivedCategory.Bind(new(itemId, name, derivedCategory, native));

            return true;
        }

        var p = native.OwnedIntParams;
        var furni = ImmutableDictionary.CreateBuilder<string, int>();
        var users = ImmutableDictionary.CreateBuilder<string, int>();
        ImmutableArray<int> parameters;
        var text = native.Text;
        int? quota = null;

        switch (name) {
            case "wf_trg_says_something":
                if (p.Length != 3 || p[0] is < 0 or > 1 || p[1] is < 0 or > 2 || p[2] is < 0 or > 1
                    || native.Text.Length > 1000) {
                    return false;
                }

                parameters = [p[1], p[2], p[0]];
                break;
            case "wf_trg_periodically":
                if (p.Length != 1 || p[0] is < 1 or > 120 || native.Text != "") {
                    return false;
                }

                parameters = [p[0]];
                break;
            case "wf_cnd_user_count_in":
                if (p.Length != 2 || p.Any(value => value is < 0 or > 125) || p[0] > p[1]
                    || native.Text != "" || native.Quantifier != 0) {
                    return false;
                }

                parameters = [p[0], p[1], 0];
                users["users"] = 0;
                break;
            case "wf_act_teleport_to":
                if (p.Length != 1 || p[0] is < 0 or > 1 || native.Text != "") {
                    return false;
                }

                parameters = [p[0], native.FurniSourceTypes[0], native.UserSourceTypes[0]];
                furni["targets"] = native.FurniSourceTypes[0];
                users["users"] = native.UserSourceTypes[0];
                break;
            case "wf_trg_walks_on_furni":
                if (p.Length != 0 || native.Text != "") {
                    return false;
                }

                parameters = [native.FurniSourceTypes[0]];
                furni["items"] = native.FurniSourceTypes[0];
                break;
            case "wf_act_show_message":
                if (p.Length != 3 || p[0] is < 0 or > 1 || !ShowStyles.Contains(p[1]) || p[2] is < -1 or > 2
                    || native.Text.Length > 200 || native.Text.Replace("\r\n", "\n").Replace('\r', '\n').Count(c => c == '\n') >= 8) {
                    return false;
                }

                parameters = [native.UserSourceTypes[0], p[0], p[1], p[2]];
                users["users"] = native.UserSourceTypes[0];
                break;
            case "wf_cnd_furnis_hv_avtrs":
                if (p.Length != 1 || p[0] is < 0 or > 1 || native.Quantifier != 0 || native.Text != "") {
                    return false;
                }

                parameters = [p[0], native.FurniSourceTypes[0]];
                furni["items"] = native.FurniSourceTypes[0];
                break;
            case "wf_act_move_rotate":
                if (p.Length != 2 || p[0] is < 0 or > 11 || p[1] is < 0 or > 3) {
                    return false;
                }

                if (!WiredMovementConfiguration.TryValidate(name,
                    new() { IntParams = [p[0], p[1], native.FurniSourceTypes[0]] }, out var movement, out _)) {
                    return false;
                }

                parameters = movement.IntParams;
                furni["movers"] = native.FurniSourceTypes[0];
                break;
            case "wf_act_move_to_dir":
                if (p.Length != 3 || p[0] is < 0 or > 7 || p[1] is < 0 or > 6 || p[2] is < 0 or > 1) {
                    return false;
                }

                parameters = [p[0], p[1], native.FurniSourceTypes[0], p[2]];
                furni["items"] = native.FurniSourceTypes[0];
                break;
            case "wf_act_control_clock":
                if (p.Length != 1 || p[0] is < 0 or > 4) {
                    return false;
                }

                parameters = [p[0], native.FurniSourceTypes[0]];
                furni["items"] = native.FurniSourceTypes[0];
                break;
            case "wf_act_join_team":
                if (p.Length != 2 || p[0] is < 1 or > 4 || p[1] is < 0 or > 2) {
                    return false;
                }

                parameters = [p[1], p[0], native.UserSourceTypes[0], 0];
                users["users"] = native.UserSourceTypes[0];
                break;
            case "wf_act_give_score":
                if (p.Length != 2 || p[0] is < -1000 or > 1000 || p[1] is < 0 or > 10) {
                    return false;
                }

                parameters = [Math.Abs(p[0]), p[0] < 0 ? 1 : 0, native.UserSourceTypes[0], p[1]];
                users["users"] = native.UserSourceTypes[0];
                quota = p[1] == 0 ? null : p[1];
                break;
            case "wf_act_set_altitude":
                if (p.Length != 2 || p[0] is < 0 or > 8000 || p[1] is < 0 or > 2) {
                    return false;
                }

                parameters = [p[1], native.FurniSourceTypes[0]];
                text = (p[0] / 100m).ToString(CultureInfo.InvariantCulture);
                furni["movers"] = native.FurniSourceTypes[0];
                break;
            case "wf_act_send_signal":
                if (p.Length != 2 || p[0] is < 0 or > 1 || p[1] is < 0 or > 1) {
                    return false;
                }

                parameters = [0, native.FurniSourceTypes[1], native.UserSourceTypes[0], p[0], p[1], 0];
                furni["items"] = native.FurniSourceTypes[0];
                furni["forwarded"] = native.FurniSourceTypes[1];
                users["users"] = native.UserSourceTypes[0];
                text = string.Join(';', native.SecondaryItems.Select(item => item.ItemId));
                break;
            case "wf_act_move_furni_as_group":
                if (p.Length != 3 || p[0] is < 0 or > 1 || p[1] is < -64 or > 64 || p[2] is < -64 or > 64) {
                    return false;
                }

                parameters = [p[0], p[1], p[2], native.FurniSourceTypes[0], native.FurniSourceTypes[1], native.UserSourceTypes[0]];
                furni["movers"] = native.FurniSourceTypes[0];
                furni["targets"] = native.FurniSourceTypes[1];
                users["users"] = native.UserSourceTypes[0];
                text = string.Join(';', native.SecondaryItems.Select(item => item.ItemId));
                break;
            default:
                return false;
        }

        var derived = new WiredConfiguration
        {
            IntParams = parameters,
            Text = text,
            SelectedItems = native.PrimaryItems.Select(item => item.ItemId).ToImmutableArray(),
            SecondarySelectedItems = native.SecondaryItems.Select(item => item.ItemId).ToImmutableArray(),
            VariableIds = native.VariableIds,
            FurniSources = furni.ToImmutable(),
            UserSources = users.ToImmutable(),
            Delay = native.Delay ?? 0,
            ScoreQuotaPerGame = quota,
            Snapshots = native.SavedState.Snapshots
        };

        if (!WiredLegacyProtocol.IsWithinLimits(derived)) {
            return false;
        }

        runtime = derived.Bind(new(itemId, name, derived, native));

        return true;
    }

    public static bool WithinBounds(WiredNativeEditorConfiguration native) => native.Version == 2
        && !native.OwnedIntParams.IsDefault && native.OwnedIntParams.Length <= WiredConfigurationLimits.IntParams
        && native.Text != null && native.Text.Length <= WiredConfigurationLimits.TextCharacters
        && System.Text.Encoding.UTF8.GetByteCount(native.Text) <= ushort.MaxValue
        && !native.PrimaryItems.IsDefault && !native.SecondaryItems.IsDefault
        && native.PrimaryItems.Length <= WiredConfigurationLimits.SelectedItems && native.SecondaryItems.Length <= WiredConfigurationLimits.SelectedItems
        && native.PrimaryItems.Concat(native.SecondaryItems).All(item => item != null && item.ItemId is > 0 and <= int.MaxValue)
        && native.PrimaryItems.Distinct().Count() == native.PrimaryItems.Length
        && native.SecondaryItems.Distinct().Count() == native.SecondaryItems.Length
        && !native.FurniSourceTypes.IsDefault && native.FurniSourceTypes.Length <= WiredConfigurationLimits.IntParams
        && !native.UserSourceTypes.IsDefault && native.UserSourceTypes.Length <= WiredConfigurationLimits.IntParams
        && !native.VariableIds.IsDefault && native.VariableIds.Length <= WiredConfigurationLimits.IntParams
        && native.VariableIds.All(token => token != null && token.Length <= 1024)
        && native.SavedState != null && !native.SavedState.Snapshots.IsDefault
        && (native.Category == WiredBoxCategory.Action ? native.Delay is >= 0 and <= WiredConfigurationLimits.DelayPulses : native.Delay == null)
        && (native.Category == WiredBoxCategory.Condition ? native.Quantifier is 0 or 1 : native.Quantifier == null)
        && (native.Category == WiredBoxCategory.Selector ? native.Filter != null && native.Inverse != null : native.Filter == null && native.Inverse == null);

    internal static bool Matches(WiredConfiguration actual, WiredConfiguration expected) => actual.Version == expected.Version
        && actual.IntParams.SequenceEqual(expected.IntParams) && actual.Text == expected.Text
        && actual.SelectedItems.SequenceEqual(expected.SelectedItems) && actual.SecondarySelectedItems.SequenceEqual(expected.SecondarySelectedItems)
        && actual.VariableIds.SequenceEqual(expected.VariableIds) && actual.Snapshots.SequenceEqual(expected.Snapshots)
        && actual.Delay == expected.Delay && actual.SelectionCode == expected.SelectionCode
        && actual.ScoreQuotaPerGame == expected.ScoreQuotaPerGame && actual.TemporaryPlacement == expected.TemporaryPlacement
        && actual.FurniSources.Count == expected.FurniSources.Count && actual.FurniSources.All(pair => expected.FurniSources.TryGetValue(pair.Key, out var value) && value == pair.Value)
        && actual.UserSources.Count == expected.UserSources.Count && actual.UserSources.All(pair => expected.UserSources.TryGetValue(pair.Key, out var value) && value == pair.Value);

    public static bool IsBound(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration runtime) =>
        runtime.Origin is { Native: not null } origin && origin.ItemId == itemId && origin.Name == descriptor.CanonicalName
        && Matches(runtime, origin.Derived)
        && TryCompile(itemId, descriptor, origin.Native, out var derived) && Matches(runtime, derived);

    public static bool TryValidateRuntime(Item item, WiredBoxDescriptor descriptor, WiredConfiguration proposed,
        out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Invalid native editor authority.";

        if (!IsBound(item.Id, descriptor, proposed)) {
            return false;
        }

        var native = proposed.Origin!.Native;
        var room = item.GetRoom();
        // Static Place templates are frozen snapshots; their original items may be picked up.
        // Custom-location anchors remain live references.
        var staticTemplates = descriptor.CanonicalName == "wf_act_place_furni" && native.FurniSourceTypes[0] == 100;
        var primaryAnchor = proposed.TemporaryPlacement is { TargetIsUser: false } placement
            && (placement.Location == WiredPlaceLocationType.CustomLocation || placement.Altitude == WiredPlaceAltitudeType.CustomAltitude)
            && native.FurniSourceTypes[1] == 100;
        var primary = staticTemplates && !primaryAnchor ? ImmutableArray<WiredNativeItemReference>.Empty : native.PrimaryItems;
        var references = primary.Concat(native.SecondaryItems).ToArray();

        // Picks are only meaningful in the live room that holds them; a pickless box has nothing to check there.
        if (references.Length == 0) {
            error = "";

            return true;
        }

        if (room == null || references.Any(reference =>
            room.GetRoomItemHandler().GetItem(reference.ItemId) is not { IsTemporary: false } picked || picked.IsWallItem != reference.Wall)) {
            return false;
        }

        bool AllAntennas(IEnumerable<WiredNativeItemReference> picks) =>
            picks.All(reference => WiredStackEngine.IsSignalAntenna(room.GetRoomItemHandler().GetItem(reference.ItemId)!));

        if (descriptor.CanonicalName is "wf_act_send_signal" or "wf_act_neg_send_signal" && native.FurniSourceTypes[0] is 100 or 101
            && !AllAntennas(native.FurniSourceTypes[0] == 100 ? native.PrimaryItems : native.SecondaryItems)) {
            error = "Signal targets must be antenna furniture.";

            return false;
        }

        if (descriptor.CanonicalName == "wf_trg_recv_signal" && proposed.FurniSources.GetValueOrDefault("items") == WiredSources.Selected
            && !AllAntennas(native.PrimaryItems)) {
            error = "wiredfurni.error.require_antenna_furni";

            return false;
        }

        error = "";

        return true;
    }

    /// <summary>The editor record every card starts from: category metadata defaults, valid by construction.</summary>
    public static WiredNativeEditorConfiguration DefaultNative(WiredBoxDescriptor descriptor, int? calendarYear = null)
    {
        var metadata = Metadata(descriptor.CanonicalName);
        var owned = metadata.OwnedDefaults;

        // The date condition's default window is the room clock's current year; the caller reads that clock once.
        if (calendarYear is { } year && descriptor.CanonicalName == "wf_cnd_match_date" && owned.Length >= 2) {
            owned = owned.SetItem(owned.Length - 2, year).SetItem(owned.Length - 1, year);
        }

        return new()
        {
            Category = descriptor.Category,
            NativeCode = descriptor.EditorCode,
            OwnedIntParams = owned,
            FurniSourceTypes = metadata.FurniDefaults,
            UserSourceTypes = metadata.UserDefaults,
            VariableIds = metadata.VariableDefaults,
            Delay = descriptor.Category == WiredBoxCategory.Action ? 0 : null,
            Quantifier = descriptor.Category == WiredBoxCategory.Condition ? 0 : null,
            Filter = descriptor.Category == WiredBoxCategory.Selector ? false : null,
            Inverse = descriptor.Category == WiredBoxCategory.Selector ? false : null
        };
    }

    /// <summary>The compiled default runtime for a fresh card, or null when the name has no native mapping yet.</summary>
    public static WiredConfiguration? DefaultRuntime(uint itemId, WiredBoxDescriptor descriptor, int? calendarYear = null) =>
        Supports(descriptor.CanonicalName) && TryCompile(itemId, descriptor, DefaultNative(descriptor, calendarYear), out var runtime) ? runtime : null;

    /// <summary>The editor record behind a bound runtime; an unbound runtime has no editor representation.</summary>
    public static bool TryProject(Item item, WiredBoxDescriptor descriptor, WiredConfiguration runtime,
        out WiredNativeEditorConfiguration native)
    {
        native = new();

        if (!Supports(descriptor.CanonicalName) || !IsBound(item.Id, descriptor, runtime)) {
            return false;
        }

        native = runtime.Origin!.Native;

        return true;
    }

    public static int SourceForRole(WiredConfiguration runtime, string role) => runtime.FurniSources.GetValueOrDefault(role);

    public static bool UsesSecondary(WiredConfiguration runtime, string role, bool legacySecondary) => runtime.Origin?.Native != null
        ? SourceForRole(runtime, role) == 101 : legacySecondary;
}
