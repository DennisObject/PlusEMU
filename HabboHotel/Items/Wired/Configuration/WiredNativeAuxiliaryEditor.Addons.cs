using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Modern.Addons;

namespace Plus.HabboHotel.Items.Wired.Configuration;

internal static partial class WiredNativeAuxiliaryEditor
{
    private static WiredNativeEditorMetadata Sources(ImmutableArray<ImmutableArray<int>> furni, ImmutableArray<ImmutableArray<int>> users,
        ImmutableArray<int> owned, int variables = 0) => new(furni, users, [.. furni.Select(group => group[0])],
        [.. users.Select(group => group[0])], owned, false)
    {
        VariableDefaults = [.. Enumerable.Repeat(Plus.HabboHotel.Items.Wired.Variables.WiredVariableAbsent.Id, variables)]
    };

    // Octane user sources accepted by the addons: trigger, clicked user, selector, signal, all users.
    private static readonly ImmutableArray<int> AddonUsers = [0, 11, 200, 201, 900];
    private static readonly ImmutableArray<int> AddonFurni = [0, 100, 200, 201, 900];
    private static readonly ImmutableArray<int> PlainFurni = [0, 100, 200, 201];
    private static readonly ImmutableArray<int> PlainUsers = [0, 11, 200, 201];

    /// <summary>The executor's own normalizer runs here, so the stored runtime is already its fixed point.</summary>
    private static WiredConfiguration? Norm(string name, WiredNativeEditorConfiguration n, ImmutableArray<int> ints)
    {
        // Only the placeholder addons carry text.
        if (n.Text.Length != 0 && !name.StartsWith("wf_xtra_text_output_", StringComparison.Ordinal)) {
            return null;
        }

        try {
            return WiredAddonConfiguration.Normalize(name, Draft(n, ints, n.Text));
        }
        catch (ArgumentException) {
            return null;
        }
    }

    private static bool In(int value, int min, int max) => value >= min && value <= max;

    /// <summary>Plain addons have no sources or variables; each field is range-checked here so the executor never clamps silently.</summary>
    private static Spec Plain(string name, ImmutableArray<int> defaults, int ints, Func<ImmutableArray<int>, bool>? valid = null,
        Func<ImmutableArray<int>, ImmutableArray<int>>? map = null) =>
        new(Meta(0, 0, defaults), n => Shape(n, ints, 0, 0, 0) && (valid?.Invoke(n.OwnedIntParams) ?? true)
            ? Norm(name, n, map?.Invoke(n.OwnedIntParams) ?? n.OwnedIntParams) : null);

    private static IEnumerable<KeyValuePair<string, Spec>> Addons()
    {
        yield return new("wf_xtra_anim_time", Plain("wf_xtra_anim_time", [500], 1, p => In(p[0], 50, 2000)));
        yield return new("wf_xtra_mov_no_animation", Plain("wf_xtra_mov_no_animation", [], 0));
        yield return new("wf_xtra_exec_in_order", Plain("wf_xtra_exec_in_order", [], 0));
        yield return new("wf_xtra_unseen", Plain("wf_xtra_unseen", [], 0));
        // Amount per window and the window in half-second pulses.
        yield return new("wf_xtra_execution_limit", Plain("wf_xtra_execution_limit", [1, 1], 2, p => In(p[0], 1, 100) && In(p[1], 1, 20)));
        // The AIR form lists skipped executions first, the picker takes the amount first.
        yield return new("wf_xtra_random", Plain("wf_xtra_random", [0, 1], 2, p => In(p[0], 0, 100) && In(p[1], 1, 100), p => [p[1], p[0]]));
        // -1 selects a comparison (index, value); 0..3 are the plain evaluation modes.
        yield return new("wf_xtra_or_eval", Plain("wf_xtra_or_eval", [0, 0, 0], 3,
            p => p[0] == -1 ? In(p[1], 0, 2) && In(p[2], 0, 1000) : In(p[0], 0, 3) && p[1] == 0 && p[2] == 0,
            p => p[0] == -1 ? [4 + p[1], 0, p[2]] : [p[0], 0, 1]));
        yield return new("wf_xtra_mov_carry_users", new(Sources([], [AddonUsers], [0]), n => Shape(n, 1, 0, 1, 0) && In(n.OwnedIntParams[0], 0, 1)
            ? Norm("wf_xtra_mov_carry_users", n, [n.OwnedIntParams[0], n.UserSourceTypes[0]]) : null));
        yield return new("wf_xtra_mov_physics", new(Sources([AddonFurni, AddonFurni], [AddonUsers], [0, 0, 0, 0]), n => Shape(n, 4, 2, 1, 0)
            && n.OwnedIntParams.All(flag => flag is 0 or 1)
            ? Norm("wf_xtra_mov_physics", n, [.. n.OwnedIntParams, n.FurniSourceTypes[0], n.FurniSourceTypes[1], n.UserSourceTypes[0]]) : null));
        yield return new("wf_xtra_text_output_furni_name", new(Sources([[0, 100, 200, 201]], [], [0]), n => Shape(n, 1, 1, 0, 0) && Placeholder(n)
            ? Norm("wf_xtra_text_output_furni_name", n, [n.OwnedIntParams[0] == 1 ? 2 : 1, n.FurniSourceTypes[0]]) : null));
        yield return new("wf_xtra_text_output_username", new(Sources([], [[0, 11, 200, 201]], [0]), n => Shape(n, 1, 0, 1, 0) && Placeholder(n)
            ? Norm("wf_xtra_text_output_username", n, [n.OwnedIntParams[0] == 1 ? 2 : 1, n.UserSourceTypes[0]]) : null));
        yield return new("wf_xtra_filter_furni", new(Meta(1, 1, [1, 0, 0], 1), FilterSpec("wf_xtra_filter_furni")));
        yield return new("wf_xtra_filter_users", new(Meta(1, 1, [1, 0, 1], 1), FilterSpec("wf_xtra_filter_users")));
        // Jump strength: literal or variable value, with the merged reference's own target and source.
        yield return new("wf_xtra_mov_curve", new(Meta(1, 1, [0, 80, 1], 1), n =>
        {
            var p = n.OwnedIntParams;

            return Shape(n, 3, 1, 1, 1) && p[0] is 0 or 1 && In(p[1], -1000, 1000) && TryTarget(p[2], out var target)
                ? Norm("wf_xtra_mov_curve", n, [7, 100, p[1], p[0], target, n.UserSourceTypes[0], n.FurniSourceTypes[0]]) : null;
        }));
        yield return new("wf_xtra_rotate_to_dir", new(Sources([[100], PlainFurni, PlainFurni], [PlainUsers, PlainUsers, PlainUsers],
            [0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0], 2), n =>
        {
            var p = n.OwnedIntParams;

            if (!Shape(n, 19, 3, 3, 2) || !TryTarget(p[5], out var timeTarget) || !TryTarget(p[17], out var distanceTarget)
                || !(p[0] is 0 or 1 && In(p[1], 0, 3) && p[2] is 0 or 1 && p[3] is 0 or 1 && In(p[4], 1, 100000)
                    && p[6] is 0 or 1 && p[7] is 0 or 1 && p[8] is 0 or 1 && In(p[9], 0, 100000) && In(p[10], 0, 7)
                    && In(p[11], 0, 127) && p[12] is 0 or 1 && p[13] is 0 or 1 && In(p[14], 0, 2) && p[15] is 0 or 1
                    && In(p[16], -64, 64) && In(p[18], -1000, 1000))) {
                return null;
            }

            int[] fields = [.. p];
            fields[5] = timeTarget;
            fields[17] = distanceTarget;

            // Animation-time variable, then distance variable, then the shooter.
            return Norm("wf_xtra_rotate_to_dir", n, [.. fields, n.UserSourceTypes[0], n.FurniSourceTypes[1], n.UserSourceTypes[2],
                n.FurniSourceTypes[2], n.UserSourceTypes[1]]);
        }));
    }

    // Count, literal-or-variable option and the variable's target; the picked variable is the opaque id.
    private static Func<WiredNativeEditorConfiguration, WiredConfiguration?> FilterSpec(string name) => n =>
    {
        var p = n.OwnedIntParams;

        return Shape(n, 3, 1, 1, 1) && In(p[0], 1, 1000) && p[1] is 0 or 1 && TryTarget(p[2], out var target)
            ? Norm(name, n, [p[0], p[1], target]) : null;
    };

    // A placeholder name of 1 to 32 characters (none yet is the inactive default), with an optional separator of at most 16.
    private static bool Placeholder(WiredNativeEditorConfiguration n)
    {
        var parts = n.Text.Split('\t');

        return n.Text.Length == 0 || parts.Length <= 2 && parts[0].Trim().Length is >= 1 and <= 32 && parts[0].IndexOfAny(['\r', '\n']) < 0
            && (parts.Length == 1 || parts[1].Length <= 16 && parts[1].IndexOfAny(['\r', '\n']) < 0);
    }
}
