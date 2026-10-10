using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Configuration;

internal static partial class WiredNativeAuxiliaryEditor
{
    // AIR merged variable targets: 0 furni, 1 user, -10 global, -20 context. The scalar executors use 0 user, 1 furni, 2 context, 3 global.
    internal static bool TryTarget(int air, out int scalar)
    {
        scalar = air switch { 1 => 0, 0 => 1, -20 => 2, -10 => 3, _ => -1 };

        return scalar >= 0;
    }

    internal static int AirTarget(int scalar) => scalar switch { 0 => 1, 1 => 0, 2 => -20, _ => -10 };

    /// <summary>Executors take a 32-bit literal in its slot; a wider value rides two extra ints [1, high].</summary>
    private static ImmutableArray<int> WithLiteral(int[] core, int literalIndex, int high, int low)
    {
        core[literalIndex] = low;

        return high == (low < 0 ? -1 : 0) ? [.. core] : [.. core, 1, high];
    }

    private static bool Shape(WiredNativeEditorConfiguration n, int ints, int furni, int users, int variables) =>
        n.OwnedIntParams.Length == ints && n.FurniSourceTypes.Length == furni && n.UserSourceTypes.Length == users
        && n.VariableIds.Length == variables;

    private static WiredConfiguration Draft(WiredNativeEditorConfiguration n, ImmutableArray<int> ints, string text = "") => new()
    {
        IntParams = ints,
        Text = text,
        SelectedItems = [.. n.PrimaryItems.Select(item => item.ItemId)],
        SecondarySelectedItems = [.. n.SecondaryItems.Select(item => item.ItemId)],
        VariableIds = n.VariableIds
    };

    // Octane comparison codes (0 >, 1 >=, 2 ==, 3 <=, 4 <, 5 !=) for the AIR radios (0 <, 1 =, 2 >, 3 <=, 4 !=, 5 >=).
    private static readonly int[] ScalarComparison = [4, 2, 0, 3, 5, 1];

    private static IEnumerable<KeyValuePair<string, Spec>> Variables()
    {
        // Destination: variable target, value (high, low), override-existing; one furni and one user source.
        yield return new("wf_act_give_var", new(Meta(1, 1, [1, 0, 0, 0], 1), n =>
        {
            var p = n.OwnedIntParams;

            if (!Shape(n, 4, 1, 1, 1) || !TryTarget(p[0], out var target)) {
                return null;
            }

            return Draft(n, WithLiteral([target, p[3], 0, n.UserSourceTypes[0], n.FurniSourceTypes[0]], 2, p[1], p[2]));
        }));
        yield return new("wf_act_remove_var", new(Meta(1, 1, [1], 1), n =>
        {
            var p = n.OwnedIntParams;

            return Shape(n, 1, 1, 1, 1) && TryTarget(p[0], out var target)
                ? Draft(n, [target, n.UserSourceTypes[0], n.FurniSourceTypes[0]]) : null;
        }));
        // Destination target, operator, literal-or-variable option, value (high, low), reference target; destination and reference sources.
        yield return new("wf_act_change_var_val", new(Meta(2, 2, [1, 0, 0, 0, 0, 1], 2), n =>
        {
            var p = n.OwnedIntParams;

            if (!Shape(n, 6, 2, 2, 2) || !TryTarget(p[0], out var target) || !TryTarget(p[5], out var reference)
                || p[2] is not (0 or 1)) {
                return null;
            }

            return Draft(n, WithLiteral([target, p[1], p[2], 0, reference, n.UserSourceTypes[0], n.FurniSourceTypes[0],
                n.UserSourceTypes[1], n.FurniSourceTypes[1]], 3, p[3], p[4]));
        }));
        yield return new("wf_cnd_has_var", new(HasVar(false), HasVarSpec));
        yield return new("wf_cnd_neg_has_var", new(HasVar(true), HasVarSpec));
        yield return new("wf_cnd_var_val_match", new(Meta(2, 2, [1, 1, 0, 0, 0, 1], 2) with { QuantifierType = 3 }, n =>
        {
            var p = n.OwnedIntParams;

            if (!Shape(n, 6, 2, 2, 2) || !TryTarget(p[0], out var target) || !TryTarget(p[5], out var reference)
                || p[1] is < 0 or > 5 || p[2] is not (0 or 1)) {
                return null;
            }

            return Draft(n, WithLiteral([target, ScalarComparison[p[1]], p[2], 0, reference, n.UserSourceTypes[0], n.FurniSourceTypes[0],
                n.UserSourceTypes[1], n.FurniSourceTypes[1], n.Quantifier ?? 0], 3, p[3], p[4]));
        }));
        // Target, comparison (< or >), creation-or-update clock, duration (high, low), time unit.
        yield return new("wf_cnd_var_age_match", new(Meta(1, 1, [1, 0, 0, 0, 0, 1], 1) with { QuantifierType = 3 }, n =>
        {
            var p = n.OwnedIntParams;

            if (!Shape(n, 6, 1, 1, 1) || !TryTarget(p[0], out var target)
                || ((long)p[3] << 32 | (uint)p[4]) is < int.MinValue or > int.MaxValue || p[1] is not (0 or 2) || p[2] is not (0 or 1)) {
                return null;
            }

            return Draft(n, [target, p[2], p[1], p[4], p[5], n.UserSourceTypes[0], n.FurniSourceTypes[0], n.Quantifier ?? 0]);
        }));
        // Variable (fixed target), comparison, value mode (none, literal, variable), value (high, low), the variable's target.
        yield return new("wf_slc_furni_with_var", new(Meta(1, 1, [1, 0, 0, 0, 1], 2), WithVariable));
        yield return new("wf_slc_users_with_var", new(Meta(1, 1, [1, 0, 0, 0, 1], 2), WithVariable));
        // Created, value changed, deleted; increased/decreased/unchanged mask; origin mask. The target comes from the chosen variable.
        yield return new("wf_trg_var_changed", new(Meta(0, 0, [1, 1, 1, 7, -1], 1), n =>
        {
            var p = n.OwnedIntParams;

            // The scalar target follows the picked variable's own target; an unchosen variable keeps the user default.
            var target = 0;

            if (!Shape(n, 5, 0, 0, 1) || !Plus.HabboHotel.Items.Wired.Variables.WiredVariableAbsent.Is(n.VariableIds[0])
                && !(Plus.HabboHotel.Items.Wired.Variables.WiredVariableDescription.TryParseCatalogId(n.VariableIds[0], out var picked, out _)
                    && (target = (int)picked) >= 0)
                || p[0] is not (0 or 1) || p[1] is not (0 or 1) || p[2] is not (0 or 1) || p[3] is < 0 or > 7) {
                return null;
            }

            var draft = Draft(n, [target, p[0], p[1], p[3] & 1, p[3] >> 1 & 1, p[3] >> 2 & 1, p[2], p[4] < 0 ? -1 : p[4] & 7]);

            return Plus.HabboHotel.Items.Wired.Variables.WiredVariableChangedTrigger.TryNormalize(draft, out var normalized, out _) ? normalized : draft;
        }));
    }

    private static WiredNativeEditorMetadata HasVar(bool negative) => Meta(1, 1, [1], 1) with { QuantifierType = 3, Invert = negative };

    private static WiredConfiguration? HasVarSpec(WiredNativeEditorConfiguration n)
    {
        var p = n.OwnedIntParams;

        return Shape(n, 1, 1, 1, 1) && TryTarget(p[0], out var target)
            ? Draft(n, [target, n.UserSourceTypes[0], n.FurniSourceTypes[0], n.Quantifier ?? 0]) : null;
    }

    private static WiredConfiguration? WithVariable(WiredNativeEditorConfiguration n)
    {
        var p = n.OwnedIntParams;

        if (!Shape(n, 5, 1, 1, 2) || n.Filter is null || n.Inverse is null || p[0] is < 0 or > 5 || p[1] is < 0 or > 2
            || !TryTarget(p[4], out var target) || p[3] < 0 != (p[2] == -1) || p[2] is not (0 or -1)) {
            return null;
        }

        // The selector executor takes a 32-bit literal; a wider one has no representation here.
        return Draft(n, [p[1] == 0 ? 0 : 1, ScalarComparison[p[0]], p[1] == 2 ? 1 : 0, p[3], target, n.UserSourceTypes[0],
            n.FurniSourceTypes[0], n.Filter.Value ? 1 : 0, n.Inverse.Value ? 1 : 0]);
    }
}
