using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Modern.Actions;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>Native records own editor data; runtime fields are checked immutable projections.</summary>
public static class WiredNativeEditorProjection
{
    internal static bool IsPristineCard(IWiredItem box, out WiredBoxDescriptor descriptor)
    {
        descriptor = null!;
        var type = box.GetType();
        var name = type == typeof(Plus.HabboHotel.Items.Wired.Boxes.Triggers.UserWalksOnBox) ? "wf_trg_walks_on_furni"
            : type == typeof(Plus.HabboHotel.Items.Wired.Boxes.Effects.ShowMessageBox) ? "wf_act_show_message"
            : type == typeof(Plus.HabboHotel.Items.Wired.Boxes.Conditions.FurniHasUsersBox) ? "wf_cnd_furnis_hv_avtrs"
            : type == typeof(Plus.HabboHotel.Items.Wired.Boxes.Triggers.RepeaterBox) ? "wf_trg_periodically"
            : type == typeof(Plus.HabboHotel.Items.Wired.Boxes.Conditions.UserCountInRoomBox) ? "wf_cnd_user_count_in"
            : type == typeof(Plus.HabboHotel.Items.Wired.Boxes.Effects.TeleportUserBox) ? "wf_act_teleport_to" : null;
        var wiredType = CardWiredType(name);
        var category = name is "wf_trg_walks_on_furni" or "wf_trg_periodically" ? WiredBoxCategory.Trigger
            : name is "wf_act_show_message" or "wf_act_teleport_to" ? WiredBoxCategory.Action : WiredBoxCategory.Condition;
        var interaction = category == WiredBoxCategory.Trigger ? InteractionType.WiredTrigger
            : category == WiredBoxCategory.Action ? InteractionType.WiredEffect : InteractionType.WiredCondition;

        return name != null && box.Type == wiredType && box.Item.Definition.WiredType == wiredType
            && box.Item.Definition.InteractionType == interaction
            && WiredLegacyEditorProjection.TryGetDescriptor(box, out descriptor)
            && descriptor.CanonicalName == name && descriptor.Category == category && descriptor.EditorCode == Code(name)
            && box.StringData == "" && box.ItemsData == "" && !box.BoolData && box.SetItems is { Count: 0 }
            && (box is not IWiredCycle cycle || cycle.TickCount == 0)
            && (box is not Plus.HabboHotel.Items.Wired.Boxes.Triggers.RepeaterBox repeat || repeat.Delay == 0)
            && (box is not Plus.HabboHotel.Items.Wired.Boxes.Effects.TeleportUserBox teleport || teleport.Delay == 0);
    }

    internal static bool TryCapturePristineCard(IWiredItem original, WiredNativeEditorConfiguration? request,
        out PristineCardSnapshot? snapshot)
    {
        snapshot = null;

        if (!IsPristineCard(original, out var descriptor)) {
            return false;
        }

        var metadata = Metadata(descriptor.CanonicalName);
        var native = new WiredNativeEditorConfiguration
        {
            Category = descriptor.Category,
            NativeCode = Code(descriptor.CanonicalName),
            OwnedIntParams = metadata.OwnedDefaults,
            FurniSourceTypes = metadata.FurniDefaults,
            UserSourceTypes = metadata.UserDefaults,
            Delay = descriptor.Category == WiredBoxCategory.Action ? 0 : null,
            Quantifier = descriptor.Category == WiredBoxCategory.Condition ? 0 : null
        };
        var picks = ImmutableArray.CreateBuilder<PristineCardPick>();

        if (request != null) {
            if (!TryCompile(original.Item.Id, descriptor, request, out _)) {
                return false;
            }

            foreach (var reference in request.PrimaryItems.Concat(request.SecondaryItems)) {
                if (reference.Wall || original.Instance.GetRoomItemHandler().GetItem(reference.ItemId)
                    is not { IsTemporary: false, IsFloorItem: true } picked) {
                    return false;
                }

                picks.Add(PristineCardPick.Capture(picked));
            }
        }

        var captured = new PristineCardSnapshot(original, original.Item, original.Instance, original.Instance.Id,
            PristineCardPick.Capture(original.Item), descriptor, original.SetItems, native, request, picks.ToImmutable());

        if (!TryCompile(original.Item.Id, descriptor, native, out _) || !captured.Matches()) {
            return false;
        }

        snapshot = captured;

        return true;
    }

    private static WiredBoxType CardWiredType(string? name) => name switch
    {
        "wf_trg_walks_on_furni" => WiredBoxType.TriggerWalkOnFurni,
        "wf_act_show_message" => WiredBoxType.EffectShowMessage,
        "wf_cnd_furnis_hv_avtrs" => WiredBoxType.ConditionFurniHasUsers,
        "wf_trg_periodically" => WiredBoxType.TriggerRepeat,
        "wf_cnd_user_count_in" => WiredBoxType.ConditionUserCountInRoom,
        "wf_act_teleport_to" => WiredBoxType.EffectTeleportToFurni,
        _ => WiredBoxType.None
    };

    internal static bool HasInitialCardConfiguration(IWiredItem box) => box switch
    {
        Plus.HabboHotel.Items.Wired.Modern.Triggers.WiredModernTimedTrigger timer => timer.HasInitialRepeatConfiguration,
        WiredModernAction action => action.HasInitialTeleportConfiguration,
        Plus.HabboHotel.Items.Wired.Modern.Conditions.WiredModernCondition condition => condition.HasInitialCountConfiguration,
        _ => false
    };

    internal static bool TryCaptureFreshCard(IWiredItem original, WiredNativeEditorConfiguration? request,
        out FreshCardSnapshot? snapshot)
    {
        snapshot = null;

        if (original is not Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernBox box
            || !HasInitialCardConfiguration(original) || box.StringData != "" || box.ItemsData != ""
            || box.BoolData || box.SetItems is not { Count: 0 }
            || box.Item.Definition.WiredType != CardWiredType(box.Descriptor.CanonicalName)
            || box.Item.Definition.WiredDescriptor is not { } definitionDescriptor
            || definitionDescriptor.CanonicalName != box.Descriptor.CanonicalName
            || definitionDescriptor.Category != box.Descriptor.Category || definitionDescriptor.EditorCode != Code(box.Descriptor.CanonicalName)
            || box.Item.Definition.InteractionType != (box.Descriptor.Category == WiredBoxCategory.Trigger ? InteractionType.WiredTrigger
                : box.Descriptor.Category == WiredBoxCategory.Action ? InteractionType.WiredEffect : InteractionType.WiredCondition)) {
            return false;
        }

        var metadata = Metadata(box.Descriptor.CanonicalName);
        var native = new WiredNativeEditorConfiguration
        {
            Category = box.Descriptor.Category,
            NativeCode = Code(box.Descriptor.CanonicalName),
            OwnedIntParams = metadata.OwnedDefaults,
            FurniSourceTypes = metadata.FurniDefaults,
            UserSourceTypes = metadata.UserDefaults,
            Delay = box.Descriptor.Category == WiredBoxCategory.Action ? 0 : null,
            Quantifier = box.Descriptor.Category == WiredBoxCategory.Condition ? 0 : null
        };
        var picks = ImmutableArray.CreateBuilder<PristineCardPick>();

        if (request != null) {
            if (!TryCompile(box.Item.Id, box.Descriptor, request, out _)) {
                return false;
            }

            foreach (var reference in request.PrimaryItems.Concat(request.SecondaryItems)) {
                if (reference.Wall || box.Instance.GetRoomItemHandler().GetItem(reference.ItemId)
                    is not { IsTemporary: false, IsFloorItem: true } picked) {
                    return false;
                }

                picks.Add(PristineCardPick.Capture(picked));
            }
        }

        var timing = box is Plus.HabboHotel.Items.Wired.Modern.Triggers.WiredModernTimedTrigger timed ? timed.InitialCardTiming : ((long, long)?)null;
        var captured = new FreshCardSnapshot(box, box.Configuration, box.Instance, box.Instance.Id,
            PristineCardPick.Capture(box.Item), box.Descriptor, definitionDescriptor, box.SetItems, timing, native, request, picks.ToImmutable());

        if (!captured.Matches()) {
            return false;
        }

        snapshot = captured;

        return true;
    }

    internal static bool TryCaptureFreshDirection(IWiredItem original, WiredNativeEditorConfiguration? request,
        out FreshDirectionSnapshot? snapshot)
    {
        snapshot = null;

        if (original is not WiredModernAction { HasInitialDirectionDraft: true } box) {
            return false;
        }

        var native = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Action,
            NativeCode = 13,
            OwnedIntParams = [0, 0, 1],
            FurniSourceTypes = [100],
            Delay = 0
        };
        var picks = ImmutableArray.CreateBuilder<NativeMovementPick>();

        if (request != null) {
            if (!TryCompile(box.Item.Id, box.Descriptor, request, out _)) {
                return false;
            }

            foreach (var reference in request.PrimaryItems.Concat(request.SecondaryItems)) {
                if (box.Instance.GetRoomItemHandler().GetItem(reference.ItemId) is not { IsTemporary: false } picked
                    || !picked.IsFloorItem || reference.Wall) {
                    return false;
                }

                picks.Add(new(reference, picked, picked.Definition, picked.Placement, picked.MovementGeneration, picked.Rotation));
            }
        }

        var captured = new FreshDirectionSnapshot(box, box.Configuration, box.Item, box.Instance, box.Instance.Id,
            box.Item.Definition, box.Descriptor, box.Item.Placement, box.Item.MovementGeneration, box.Item.Rotation, native, request, picks.ToImmutable());

        if (!captured.Matches()) {
            return false;
        }

        snapshot = captured;

        return true;
    }

    internal static bool TryCaptureLegacyRotate(IWiredItem original, out LegacyRotateSnapshot? snapshot)
    {
        snapshot = null;

        if (original.GetType() != typeof(Plus.HabboHotel.Items.Wired.Boxes.Effects.MoveAndRotateBox)
            || original is not Plus.HabboHotel.Items.Wired.Boxes.Effects.MoveAndRotateBox box
            || box.StringData != "" || box.ItemsData == null || box.ItemsData.Length > WiredConfigurationLimits.TextCharacters
            || !WiredLegacyEditorProjection.TryGetDescriptor(box, out var descriptor)
            || descriptor.CanonicalName != "wf_act_move_rotate") {
            return false;
        }

        var dictionary = box.SetItems;
        var picks = dictionary.ToArray().ToImmutableArray();
        var native = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Action,
            NativeCode = 4,
            OwnedIntParams = [0, 0],
            PrimaryItems = picks.Select(pick => new WiredNativeItemReference(pick.Key, false)).ToImmutableArray(),
            FurniSourceTypes = [100],
            Delay = box.Delay,
            DormantLegacy = new() { LegacyRotateBool = box.BoolData, LegacyRotateItemsData = box.ItemsData }
        };
        var captured = new LegacyRotateSnapshot(box, box.Item, box.Instance, box.Item.Definition, descriptor,
            box.Item.Placement, dictionary, picks, box.BoolData, box.ItemsData, box.Delay, native);

        if (!TryCompile(box.Item.Id, descriptor, native, out _) || !captured.Matches()) {
            return false;
        }

        snapshot = captured;

        return true;
    }

    internal static bool TryCaptureLegacyJoin(IWiredItem original, out LegacyJoinSnapshot? snapshot)
    {
        snapshot = null;

        if (original.GetType() != typeof(Plus.HabboHotel.Items.Wired.Boxes.Effects.AddActorToTeamBox)
            || original is not Plus.HabboHotel.Items.Wired.Boxes.Effects.AddActorToTeamBox box
            || !WiredLegacyEditorProjection.TryGetDescriptor(box, out var descriptor)
            || descriptor.CanonicalName != "wf_act_join_team"
            || box.StringData == null || box.ItemsData == null
            || box.StringData.Length > WiredConfigurationLimits.TextCharacters
            || box.ItemsData.Length > WiredConfigurationLimits.TextCharacters) {
            return false;
        }

        var fresh = box.StringData.Length == 0;
        var team = 1;

        if (!fresh && (!int.TryParse(box.StringData, NumberStyles.None, CultureInfo.InvariantCulture, out team)
            || team is < 1 or > 4)) {
            return false;
        }

        var dictionary = box.SetItems;
        var picks = dictionary.ToArray().ToImmutableArray();
        var native = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Action,
            NativeCode = Code(descriptor.CanonicalName),
            OwnedIntParams = [team, fresh ? 0 : 2],
            Delay = 0,
            UserSourceTypes = [0],
            PrimaryItems = picks.Select(pick => new WiredNativeItemReference(pick.Key, false)).ToImmutableArray(),
            DormantLegacy = new()
            {
                LegacyJoinTeam = box.StringData,
                LegacyJoinBool = box.BoolData,
                LegacyJoinItemsData = box.ItemsData
            }
        };
        var captured = new LegacyJoinSnapshot(box, box.Item, box.Instance, box.Item.Definition, descriptor,
            dictionary, picks, box.StringData, box.BoolData, box.ItemsData, native);

        if (!WithinBounds(native) || !TryCompile(box.Item.Id, descriptor, native, out _) || !captured.Matches()) {
            return false;
        }

        snapshot = captured;

        return true;
    }

    internal static bool TryCaptureLegacySays(IWiredItem original, out LegacySaysSnapshot? snapshot)
    {
        snapshot = null;

        if (original.GetType() != typeof(Plus.HabboHotel.Items.Wired.Boxes.Triggers.UserSaysBox)
            || original is not Plus.HabboHotel.Items.Wired.Boxes.Triggers.UserSaysBox box
            || !WiredLegacyEditorProjection.TryGetDescriptor(box, out var descriptor)
            || descriptor.CanonicalName != "wf_trg_says_something" || box.StringData != ""
            || box.ItemsData == null || box.ItemsData.Length > WiredConfigurationLimits.TextCharacters) {
            return false;
        }

        var dictionary = box.SetItems;
        var picks = dictionary.ToArray().ToImmutableArray();
        var native = new WiredNativeEditorConfiguration
        {
            Category = WiredBoxCategory.Trigger,
            NativeCode = 0,
            OwnedIntParams = [box.BoolData ? 1 : 0, 0, 0],
            PrimaryItems = picks.Select(pick => new WiredNativeItemReference(pick.Key, false)).ToImmutableArray(),
            DormantLegacy = new() { LegacySaysBool = box.BoolData, LegacySaysItemsData = box.ItemsData }
        };
        var captured = new LegacySaysSnapshot(box, box.Item, box.Instance, box.Item.Definition, descriptor,
            dictionary, picks, box.BoolData, box.ItemsData, native);

        if (!WithinBounds(native) || !TryCompile(box.Item.Id, descriptor, native, out _) || !captured.Matches()) {
            return false;
        }

        snapshot = captured;

        return true;
    }

    private static readonly int[] ShowStyles = [34, 200, 201, 202, 210, 211, 212, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 250, 251, 252];

    public static bool Supports(string name) => name == "wf_trg_says_something" || Code(name) != 0;

    public static int Code(string name) => name switch
    {
        "wf_trg_says_something" => 0,
        "wf_trg_walks_on_furni" => 1,
        "wf_trg_periodically" => 6,
        "wf_cnd_user_count_in" => 5,
        "wf_act_teleport_to" => 8,
        "wf_act_show_message" => 7,
        "wf_cnd_furnis_hv_avtrs" => 1,
        "wf_act_move_rotate" => 4,
        "wf_act_move_to_dir" => 13,
        "wf_act_control_clock" => 28,
        "wf_act_join_team" => 9,
        "wf_act_give_score" => 6,
        "wf_act_set_altitude" => 29,
        "wf_act_send_signal" => 30,
        "wf_act_move_furni_as_group" => 57,
        _ => 0
    };

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
        _ => throw new InvalidDataException("This native editor projection is unavailable.")
    };

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
            || native.VariableIds.Length != 0) {
            return false;
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

        if (name is "wf_act_move_rotate" or "wf_act_move_to_dir" or "wf_trg_walks_on_furni" or "wf_act_show_message" or "wf_cnd_furnis_hv_avtrs" or "wf_trg_periodically" or "wf_cnd_user_count_in" or "wf_act_teleport_to" && native.DormantLegacy is { } dormant) {
            if (name != "wf_act_show_message") {
                text = dormant.Text;
            }

            // Teleport's legacy movers slot stays serialized in DormantLegacy, never executable.
            foreach (var pair in dormant.FurniSources.Where(pair => !furni.ContainsKey(pair.Key)
                && !(name == "wf_act_teleport_to" && pair.Key == "movers"))) {
                furni[pair.Key] = pair.Value;
            }

            foreach (var pair in dormant.UserSources.Where(pair => !users.ContainsKey(pair.Key))) {
                users[pair.Key] = pair.Value;
            }
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

        runtime = derived.Bind(new(WiredConfigurationOriginKind.Native, itemId, name, derived, native));

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
        runtime.Origin is { } origin && origin.ItemId == itemId && origin.Name == descriptor.CanonicalName
        && Matches(runtime, origin.Derived)
        && (origin.Kind == WiredConfigurationOriginKind.StoredLegacy
            ? origin.Native == null && origin.StoredLegacy != null
            : origin.Kind == WiredConfigurationOriginKind.Native && origin.Native != null
                && TryCompile(itemId, descriptor, origin.Native, out var derived) && Matches(runtime, derived));

    public static bool TryValidateRuntime(Item item, WiredBoxDescriptor descriptor, WiredConfiguration proposed,
        out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Invalid native editor authority.";

        if (!IsBound(item.Id, descriptor, proposed)) {
            return false;
        }

        if (proposed.Origin!.Native is { } native) {
            var room = item.GetRoom();

            if (room == null || native.PrimaryItems.Concat(native.SecondaryItems).Any(reference =>
                room.GetRoomItemHandler().GetItem(reference.ItemId) is not { IsTemporary: false } picked || picked.IsWallItem != reference.Wall)) {
                return false;
            }

            if (descriptor.CanonicalName == "wf_act_send_signal" && native.FurniSourceTypes[0] is 100 or 101) {
                var antennaIds = native.FurniSourceTypes[0] == 100 ? native.PrimaryItems : native.SecondaryItems;

                if (antennaIds.Any(reference => !WiredStackEngine.IsSignalAntenna(room.GetRoomItemHandler().GetItem(reference.ItemId)!))) {
                    error = "Signal targets must be antenna furniture.";

                    return false;
                }
            }
        }

        error = "";

        return true;
    }

    internal static WiredConfiguration TrustLegacy(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration stored)
    {
        if (!WiredLegacyProtocol.IsWithinLimits(stored)) {
            throw new InvalidDataException("Invalid legacy Wired configuration.");
        }

        // The owning box validates and normalizes a legacy row before its trusted installation.
        return stored.Bind(new(WiredConfigurationOriginKind.StoredLegacy, itemId, descriptor.CanonicalName, stored, null, stored));
    }

    internal static WiredConfiguration RebindLegacy(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration previous, WiredConfiguration normalized)
    {
        if (previous.Origin is not { Kind: WiredConfigurationOriginKind.StoredLegacy, Native: null, StoredLegacy: not null } origin || origin.ItemId != itemId || origin.Name != descriptor.CanonicalName
            || !Matches(previous, origin.Derived)) {
            throw new InvalidDataException("Legacy origin cannot be inferred from an unbound runtime draft.");
        }

        return normalized.Bind(new(WiredConfigurationOriginKind.StoredLegacy, itemId, descriptor.CanonicalName, normalized, null, origin.StoredLegacy));
    }

    public static bool TryProject(Item item, WiredBoxDescriptor descriptor, WiredConfiguration runtime,
        out WiredNativeEditorConfiguration native)
    {
        native = new();
        var name = descriptor.CanonicalName;

        if (!Supports(name)) {
            return false;
        }

        var metadata = Metadata(name);

        if (name is not ("wf_act_move_to_dir" or "wf_trg_walks_on_furni" or "wf_act_show_message" or "wf_cnd_furnis_hv_avtrs" or "wf_trg_periodically" or "wf_cnd_user_count_in" or "wf_act_teleport_to") && runtime.Origin == null && runtime.IntParams.Length == 0) {
            native = new()
            {
                Category = descriptor.Category,
                NativeCode = Code(name),
                OwnedIntParams = metadata.OwnedDefaults,
                FurniSourceTypes = metadata.FurniDefaults,
                UserSourceTypes = metadata.UserDefaults,
                Delay = descriptor.Category == WiredBoxCategory.Action ? 0 : null
            };

            return true;
        }

        if (!IsBound(item.Id, descriptor, runtime)) {
            return false;
        }

        if (runtime.Origin!.Native is { } existing) {
            native = existing;

            return true;
        }

        var p = runtime.IntParams;
        ImmutableArray<int> owned;
        ImmutableArray<int> furni = [];
        ImmutableArray<int> users = [];

        switch (name) {
            case "wf_trg_says_something" when p.Length == 3 && runtime.Delay == 0 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                owned = [p[2], p[0], p[1]];
                break;
            case "wf_trg_periodically" when p.Length == 1 && runtime.Delay == 0 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                owned = [p[0]];
                break;
            case "wf_cnd_user_count_in" when p.Length == 3 && p[2] == 0 && runtime.Delay == 0 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                owned = [p[0], p[1]];
                break;
            case "wf_act_teleport_to" when p.Length == 3 && p[1] == 100 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                owned = [p[0]];
                furni = [p[1]];
                users = [p[2]];
                break;
            case "wf_trg_walks_on_furni" when p.Length == 1 && runtime.Delay == 0 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                owned = [];
                furni = [p[0]];
                break;
            case "wf_act_show_message" when p.Length is 3 or 4 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                owned = [p[1], p[2], p.Length == 4 ? p[3] : -1];
                users = [p[0]];
                break;
            case "wf_cnd_furnis_hv_avtrs" when p.Length == 2 && runtime.Delay == 0 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                owned = [p[0]];
                furni = [p[1]];
                break;
            case "wf_act_move_rotate" when p.Length == 4 && p[3] == 0 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                var editor = WiredMovementConfiguration.ForEditor(name, runtime).IntParams;

                if (editor.Length != 3) {
                    return false;
                }

                owned = [editor[0], editor[1]];
                furni = [p[2]];
                break;
            case "wf_act_move_to_dir" when p.Length == 4 && runtime.SelectionCode == 0
                && runtime.ScoreQuotaPerGame == null && runtime.TemporaryPlacement == null:
                owned = [p[0], p[1], p[3]];
                furni = [p[2]];
                break;
            case "wf_act_control_clock" when p.Length == 2:
                owned = [p[0]];
                furni = [p[1]];
                break;
            case "wf_act_join_team" when p.Length == 4 && p[3] == 0:
                owned = [p[1], p[0]];
                users = [p[2]];
                break;
            case "wf_act_give_score" when p.Length is 3 or 4:
                owned = [p[1] == 1 ? -p[0] : p[0], runtime.ScoreQuotaPerGame ?? 0];
                users = [p[2]];
                break;
            case "wf_act_set_altitude" when p.Length == 2:
                if (!decimal.TryParse(runtime.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var altitude)
                    || altitude * 100m != decimal.Truncate(altitude * 100m) || altitude is < 0 or > 80) {
                    return false;
                }

                owned = [(int)(altitude * 100m), p[0]];
                furni = [p[1]];
                break;
            case "wf_act_send_signal" when p.Length == 6 && p[0] == 0:
                owned = [p[3], p[4]];
                furni = [100, runtime.FurniSources.GetValueOrDefault("forwarded", p[1]) == 100 ? 101 : p[1]];
                users = [p[2]];
                break;
            case "wf_act_move_furni_as_group" when p.Length == 6:
                owned = [p[0], p[1], p[2]];
                furni = [p[3], p[4] == 100 ? 101 : p[4]];
                users = [p[5]];
                break;
            default:
                return false;
        }

        var room = item.GetRoom();

        if (room == null || !TryReferences(runtime.SelectedItems, out var primary)
            || !TryReferences(runtime.SecondarySelectedItems, out var secondary)) {
            return false;
        }

        native = new()
        {
            Category = descriptor.Category,
            NativeCode = Code(name),
            OwnedIntParams = owned,
            Text = name is "wf_trg_periodically" or "wf_cnd_user_count_in" or "wf_act_teleport_to" or "wf_trg_walks_on_furni" or "wf_cnd_furnis_hv_avtrs" or "wf_act_set_altitude" or "wf_act_send_signal" or "wf_act_move_furni_as_group" or "wf_act_move_rotate" or "wf_act_move_to_dir" ? "" : runtime.Text,
            PrimaryItems = primary,
            SecondaryItems = secondary,
            FurniSourceTypes = furni,
            UserSourceTypes = users,
            VariableIds = runtime.VariableIds,
            Delay = descriptor.Category == WiredBoxCategory.Action ? runtime.Delay : null,
            Quantifier = descriptor.Category == WiredBoxCategory.Condition ? 0 : null,
            SavedState = new() { Snapshots = runtime.Snapshots },
            DormantLegacy = new()
            {
                JoinMode = name == "wf_act_join_team" ? p[3] : null,
                FurniSources = runtime.Origin.StoredLegacy?.FurniSources ?? runtime.FurniSources,
                UserSources = runtime.Origin.StoredLegacy?.UserSources ?? runtime.UserSources,
                Text = runtime.Origin.StoredLegacy?.Text ?? runtime.Text
            }
        };

        // The inverse is admitted only if its executable native fields represent the old active fields exactly.
        if (!TryCompile(item.Id, descriptor, native, out var projected)
            || !runtime.SelectedItems.SequenceEqual(projected.SelectedItems)
            || !runtime.SecondarySelectedItems.SequenceEqual(projected.SecondarySelectedItems)
            || name is "wf_act_move_rotate" or "wf_act_move_to_dir" && !runtime.IntParams.SequenceEqual(projected.IntParams)
            || name is "wf_trg_walks_on_furni" or "wf_act_show_message" or "wf_cnd_furnis_hv_avtrs" or "wf_trg_periodically" or "wf_cnd_user_count_in" or "wf_act_teleport_to"
                && (!runtime.IntParams.SequenceEqual(name == "wf_act_show_message" && p.Length == 3 ? projected.IntParams.Take(3) : projected.IntParams)
                    || name == "wf_act_show_message" && p.Length == 3 && projected.IntParams[3] != -1
                    || runtime.Text != projected.Text || runtime.Delay != projected.Delay
                    || !runtime.Snapshots.SequenceEqual(projected.Snapshots)
                    || runtime.FurniSources.Any(pair => projected.FurniSources.GetValueOrDefault(pair.Key, int.MinValue) != pair.Value)
                    || runtime.UserSources.Any(pair => projected.UserSources.GetValueOrDefault(pair.Key, int.MinValue) != pair.Value))
            || runtime.FurniSources.Any(pair => !projected.FurniSources.ContainsKey(pair.Key))
            || runtime.UserSources.Any(pair => !projected.UserSources.ContainsKey(pair.Key))) {
            return false;
        }

        return true;

        bool TryReferences(ImmutableArray<uint> ids, out ImmutableArray<WiredNativeItemReference> references)
        {
            var builder = ImmutableArray.CreateBuilder<WiredNativeItemReference>(ids.Length);

            foreach (var id in ids) {
                if (room.GetRoomItemHandler().GetItem(id) is not { IsTemporary: false } selected) {
                    references = [];

                    return false;
                }

                builder.Add(new(id, selected.IsWallItem));
            }

            references = builder.MoveToImmutable();

            return true;
        }
    }

    public static bool SameBody(WiredNativeEditorConfiguration left, WiredNativeEditorConfiguration right) =>
        left.Category == right.Category && left.NativeCode == right.NativeCode
        && left.OwnedIntParams.SequenceEqual(right.OwnedIntParams) && left.Text == right.Text
        && left.PrimaryItems.SequenceEqual(right.PrimaryItems) && left.SecondaryItems.SequenceEqual(right.SecondaryItems)
        && left.FurniSourceTypes.SequenceEqual(right.FurniSourceTypes) && left.UserSourceTypes.SequenceEqual(right.UserSourceTypes)
        && left.VariableIds.SequenceEqual(right.VariableIds) && left.Delay == right.Delay && left.Quantifier == right.Quantifier
        && left.Filter == right.Filter && left.Inverse == right.Inverse;

    public static int SourceForRole(WiredConfiguration runtime, string role) => runtime.FurniSources.GetValueOrDefault(role);

    public static bool UsesSecondary(WiredConfiguration runtime, string role, bool legacySecondary) => runtime.Origin?.Native != null
        ? SourceForRole(runtime, role) == 101 : legacySecondary;
}
