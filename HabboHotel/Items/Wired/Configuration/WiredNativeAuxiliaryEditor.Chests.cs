using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Configuration;

internal static partial class WiredNativeAuxiliaryEditor
{
    // Chest furni selections: picked group (100/101), live trigger furni, selector or signal; users never include bots here.
    private static readonly ImmutableArray<int> ChestFurni = [0, 100, 101, 200, 201];
    private static readonly ImmutableArray<int> ChestUsers = [0, 11, 200, 201];

    private static WiredNativeEditorMetadata ChestMeta(int furniSelections, int userSelections, ImmutableArray<int> owned, int variables,
        ImmutableArray<int> furniDefaults, ImmutableArray<int> userDefaults) => new(
            [.. Enumerable.Repeat(ChestFurni, furniSelections)], [.. Enumerable.Repeat(ChestUsers, userSelections)],
            furniDefaults, userDefaults, owned, false)
    {
        VariableDefaults = [.. Enumerable.Repeat(Plus.HabboHotel.Items.Wired.Variables.WiredVariableAbsent.Id, variables)]
    };

    /// <summary>Selections become indexed role sources ("f0", "u1", ...) the chest boxes read.</summary>
    private static WiredConfiguration ChestDraft(WiredNativeEditorConfiguration n, int[] ints, string text = "") => Draft(n, [.. ints], text) with
    {
        FurniSources = n.FurniSourceTypes.Select((source, index) => (source, index)).ToImmutableDictionary(pair => "f" + pair.index, pair => pair.source),
        UserSources = n.UserSourceTypes.Select((source, index) => (source, index)).ToImmutableDictionary(pair => "u" + pair.index, pair => pair.source)
    };

    private static IEnumerable<KeyValuePair<string, Spec>> Chests()
    {
        // Mode (amount or all), amount, literal-or-variable option, its target, show-by-default, then currency category / iteration order.
        yield return new("wf_act_give_currency", new(ChestMeta(2, 2, [0, 1, 0, 1, 1, 11], 1, [100, 0], [0, 0]), n => Reward(n, 11, 13)));
        yield return new("wf_act_give_furni", new(ChestMeta(2, 2, [0, 1, 0, 1, 0, 0], 1, [100, 0], [0, 0]), n => Reward(n, 0, 2)));
        // Mode, multiplier, option, target, timeout flag and seconds; chests, contracts and the merged reference.
        yield return new("wf_act_init_transaction", new(ChestMeta(3, 2, [0, 1, 0, 1, 0, 300], 1, [100, 100, 0], [0, 0]), n =>
        {
            var p = n.OwnedIntParams;

            return Shape(n, 6, 3, 2, 1) && TryTarget(p[3], out var target) ? ChestDraft(n, [p[0], p[1], p[2], target, p[4], p[5]], n.Text) : null;
        }));
        yield return new("wf_act_cancel_transaction", new(ChestMeta(1, 1, [0], 0, [100], [0]), n =>
            Shape(n, 1, 1, 1, 0) ? ChestDraft(n, [.. n.OwnedIntParams]) : null));
        // Compared amount, its option and target, comparison (the form's own codes).
        yield return new("wf_cnd_chest_has_items", new(ChestMeta(2, 1, [0, 0, 1, 2], 1, [100, 0], [0]) with { QuantifierType = 0 }, HasChest(2)));
        yield return new("wf_cnd_chest_has_item_type", new(ChestMeta(3, 1, [0, 0, 1, 2], 1, [100, 100, 0], [0]) with { QuantifierType = 0 }, HasChest(3)));
        yield return new("wf_trg_transaction_complete", new(Meta(0, 0, []), n => Shape(n, 0, 0, 0, 0) ? ChestDraft(n, []) : null));
        yield return new("wf_trg_transaction_fail", new(Meta(0, 0, []), n => Shape(n, 0, 0, 0, 0) ? ChestDraft(n, []) : null));
        // Scanning mode; item types and chests, writing into a context variable.
        yield return new("wf_xtra_scan_chest_furni_by_type", new(ChestMeta(2, 0, [0], 1, [100, 100], []), n =>
            Shape(n, 1, 2, 0, 1) ? ChestDraft(n, [.. n.OwnedIntParams]) : null));
        // Payment then reward: enabled, element type, option, amount, target; payment/reward furni, then the merged references.
        yield return new("wf_xtra_custom_contract", new(ChestMeta(4, 2, [0, 0, 0, 1, 1, 0, 0, 0, 1, 1], 2, [100, 100, 0, 0], [0, 0]), n =>
        {
            var p = n.OwnedIntParams;

            return Shape(n, 10, 4, 2, 2) && TryTarget(p[4], out var payment) && TryTarget(p[9], out var reward)
                ? ChestDraft(n, [p[0], p[1], p[2], p[3], payment, p[5], p[6], p[7], p[8], reward]) : null;
        }));
    }

    // Reward mode, amount, option, target, show-by-default, then the category or iteration order; the popup text rides along.
    private static WiredConfiguration? Reward(WiredNativeEditorConfiguration n, int lowest, int highest)
    {
        var p = n.OwnedIntParams;

        return Shape(n, 6, 2, 2, 1) && TryTarget(p[3], out var target) && p[5] >= lowest && p[5] <= highest
            ? ChestDraft(n, [p[0], p[1], p[2], target, p[4], p[5]], n.Text) : null;
    }

    private static Func<WiredNativeEditorConfiguration, WiredConfiguration?> HasChest(int furni) => n =>
    {
        var p = n.OwnedIntParams;

        return Shape(n, 4, furni, 1, 1) && TryTarget(p[2], out var target) ? ChestDraft(n, [p[0], p[1], target, p[3]]) : null;
    };
}
