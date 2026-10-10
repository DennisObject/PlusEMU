using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Executable scalar boxes decoded from the native editor's compiled runtime shape.</summary>
public sealed class WiredVariableExecutors(WiredVariableModule variables, TimeProvider clock)
{
    public static bool Supports(string name) => name is "wf_act_give_var" or "wf_act_remove_var" or "wf_act_change_var_val"
        or "wf_cnd_has_var" or "wf_cnd_neg_has_var" or "wf_cnd_var_val_match" or "wf_cnd_var_age_match";

    /// <summary>Shape-only validation: save and load must not depend on which definition boxes have been loaded yet.</summary>
    public static bool TryValidate(string name, WiredConfiguration configuration, out string error)
    {
        error = "Invalid scalar variable settings.";

        return TryDecode(name, configuration, out var core, out _) && TryTokens(name, configuration, core, null, out var tokens)
            && ValidateCore(name, configuration, core, tokens, out error);
    }

    /// <summary>
    /// The native picker saves opaque catalog ids. Without an authority they only have to be well formed for their target;
    /// with one (execution) each resolves through the room module's current catalog, so a deleted, foreign or retargeted
    /// variable resolves to nothing. An unchosen id stays empty and keeps the box inactive.
    /// </summary>
    private static bool TryTokens(string name, WiredConfiguration configuration, System.Collections.Immutable.ImmutableArray<int> p,
        WiredVariableModule? authority, out string[] tokens)
    {
        tokens = ["", ""];
        var ids = configuration.VariableIds;
        var operand = name is "wf_act_change_var_val" or "wf_cnd_var_val_match";

        if (ids.Length != (operand ? 2 : 1) || !Enum.IsDefined((WiredVariableTarget)p[0])) {
            return false;
        }

        bool Resolve(string id, int target, out string token)
        {
            token = "";

            if (WiredVariableAbsent.Is(id)) {
                return true;
            }

            if (!WiredVariableDescription.TryParseCatalogId(id, out var parsed, out var parsedToken) || (int)parsed != target) {
                return false;
            }

            token = authority is null ? parsedToken : authority.TryResolveCatalogId(id, parsed, out var reference) ? reference.Token : "";

            return true;
        }

        if (!Resolve(ids[0], p[0], out tokens[0])) {
            return false;
        }

        // A literal-valued box ignores its reference slot.
        if (operand && p[2] == 1) {
            return Enum.IsDefined((WiredVariableTarget)p[4]) && Resolve(ids[1], p[4], out tokens[1]);
        }

        return true;
    }

    private static bool TryDecode(string name, WiredConfiguration configuration, out System.Collections.Immutable.ImmutableArray<int> core, out long literal)
    {
        core = configuration.IntParams;
        literal = 0;
        var count = name switch
        {
            "wf_act_give_var" => 5,
            "wf_act_remove_var" => 3,
            "wf_act_change_var_val" => 9,
            "wf_cnd_has_var" or "wf_cnd_neg_has_var" => 4,
            "wf_cnd_var_val_match" => 10,
            "wf_cnd_var_age_match" => 8,
            _ => -1
        };
        var literalIndex = name switch { "wf_act_give_var" => 2, "wf_act_change_var_val" or "wf_cnd_var_val_match" => 3, _ => -1 };

        if (core.Length == count + 2 && literalIndex >= 0 && core[count] == 1) {
            literal = ((long)core[count + 1] << 32) | (uint)core[literalIndex];
            core = core[..count];

            return true;
        }

        if (core.Length != count) {
            return false;
        }

        if (literalIndex >= 0) {
            literal = core[literalIndex];
        }

        return true;
    }

    private static bool ValidateCore(string name, WiredConfiguration configuration, System.Collections.Immutable.ImmutableArray<int> p,
        string[] tokens, out string error)
    {
        error = "Invalid scalar variable settings.";

        if (!Supports(name) || configuration.Version != 1 || !Enum.IsDefined((WiredVariableTarget)p[0])) {
            return false;
        }

        if (!ValidToken(tokens[0])) {
            return false;
        }

        if (name is "wf_act_give_var" or "wf_act_remove_var") {
            if ((!WiredVariableModule.TryDefinitionId(tokens[0], out _)
                && !RoomWiredBuiltinVariables.SupportsPresenceMutation(new((WiredVariableTarget)p[0], tokens[0]))) || p[0] == 3) {
                return false;
            }

            if (name == "wf_act_give_var" && p[1] is not (0 or 1)) {
                return false;
            }
        }

        if (name is "wf_act_change_var_val" or "wf_cnd_var_val_match") {
            if (name == "wf_act_change_var_val" ? !WiredVariableArithmetic.IsSupported(p[1]) : p[1] is < 0 or > 5) {
                return false;
            }

            if (p[2] is not (0 or 1) || !Enum.IsDefined((WiredVariableTarget)p[4])) {
                return false;
            }

            if (p[2] == 1 && !ValidToken(tokens[1])) {
                return false;
            }
        }

        if (name == "wf_cnd_var_age_match" && (p[1] is not (0 or 1) || p[2] is not (0 or 2) || p[3] is < 0 or > 1000000 || p[4] is < 0 or > 7)) {
            return false;
        }

        if (name.StartsWith("wf_cnd_", StringComparison.Ordinal) && p[^1] is not (0 or 1)) {
            return false;
        }

        var sourceIndices = name switch
        {
            "wf_act_give_var" => new[] { 3, 4 },
            "wf_act_remove_var" => [1, 2],
            "wf_act_change_var_val" or "wf_cnd_var_val_match" => [5, 6, 7, 8],
            "wf_cnd_var_age_match" => [5, 6],
            _ => [1, 2]
        };

        if (sourceIndices.Any(i => p[i] is not (0 or 11 or 100 or 101 or 200 or 201))) {
            return false;
        }

        error = "";

        return true;
    }

    public bool Execute(string name, WiredConfiguration configuration, WiredVariableFrame frame)
    {
        if (!TryDecode(name, configuration, out var p, out var literal) || !TryTokens(name, configuration, p, variables, out var tokens)
            || !ValidateCore(name, configuration, p, tokens, out _)) {
            return false;
        }

        var reference = new WiredVariableReference((WiredVariableTarget)p[0], tokens[0]);
        var sourceStart = name switch
        {
            "wf_act_give_var" => 3,
            "wf_act_remove_var" => 1,
            "wf_act_change_var_val" or "wf_cnd_var_val_match" or "wf_cnd_var_age_match" => 5,
            _ => 1
        };
        var targets = Select(frame, reference.Target, p[sourceStart], p[sourceStart + 1], configuration.SelectedItems).ToArray();

        if (name is "wf_act_give_var" or "wf_act_remove_var") {
            var mutation = name == "wf_act_remove_var" ? WiredVariableMutation.Remove : p[1] == 1 ? WiredVariableMutation.Replace : WiredVariableMutation.Give;
            var changed = false;

            foreach (var target in targets) {
                changed |= variables.Mutate(reference, target, mutation, name == "wf_act_remove_var" ? 0 : literal, frame);
            }

            return changed;
        }

        if (name is "wf_cnd_has_var" or "wf_cnd_neg_has_var") {
            var outcomes = targets.Select(target => variables.Read(reference, target, frame) is not null).ToArray();

            if (outcomes.Length == 0) {
                return false;
            }

            var matches = Quantify(outcomes, p[^1]);

            return name == "wf_cnd_neg_has_var" ? !matches : matches;
        }

        if (name == "wf_cnd_var_age_match") {
            // Octane duration units: milliseconds, seconds, minutes, hours, days, weeks, months, years.
            long[] units = [1, 1000, 60000, 3600000, 86400000, 604800000, 2592000000, 31536000000];
            var durationTicks = (decimal)p[3] * units[p[4]] * TimeSpan.TicksPerMillisecond;
            var now = clock.GetUtcNow();

            return Quantify(targets.Select(target =>
            {
                var current = variables.Read(reference, target, frame);

                if (current is null) {
                    return false;
                }

                var timestamp = p[1] == 1 ? current.UpdatedAt : current.CreatedAt;

                if (timestamp is null) {
                    return false;
                }

                var ageTicks = now <= timestamp.Value ? 0 : (now - timestamp.Value).Ticks;

                return p[2] == 0 ? ageTicks < durationTicks : ageTicks > durationTicks;
            }), p[^1]);
        }

        var operands = new List<(WiredVariableHolder Holder, long Value)>();

        if (p[2] == 1 && !(name == "wf_act_change_var_val" && WiredVariableArithmetic.IsUnary(p[1]))) {
            var operandReference = new WiredVariableReference((WiredVariableTarget)p[4], tokens[1]);
            var picked = configuration.SecondarySelectedItems;
            using var reads = variables.CaptureReads([operandReference], frame);

            foreach (var source in Select(frame, operandReference.Target, p[7], p[8], picked)) {
                if (reads.Read(operandReference, source, frame) is { } value) {
                    operands.Add((source, value.Value));
                }
            }

            if (operands.Count == 0) {
                return false;
            }
        }

        var operand = operands.Count == 0 ? literal : operands[0].Value;

        if (name == "wf_cnd_var_val_match") {
            return Quantify(targets.Select(target => variables.Read(reference, target, frame) is { } value
                && WiredVariablePredicates.Compare(p[1], value.Value, operand)), p[^1]);
        }

        var any = false;

        for (var index = 0; index < targets.Length; index++) {
            if (frame.VariableChanges is { } batch) {
                if (variables.Read(reference, targets[index], frame) is not null) {
                    batch.Add(variables, reference, targets[index], p[1], operand, frame);
                    any = true;
                }
            }
            else {
                any |= variables.Change(reference, targets[index], WiredVariableMutation.Set, current => WiredVariableArithmetic.Apply(p[1], current, operand), frame, notifyUnchanged: true);
            }
        }

        return any;
    }

    public static IEnumerable<WiredVariableHolder> Select(WiredVariableFrame frame, WiredVariableTarget target,
        int userSource, int furniSource, IEnumerable<uint> picked)
    {
        if (target is WiredVariableTarget.Context or WiredVariableTarget.Global) {
            return [new(target, 0, 0)];
        }

        var source = target == WiredVariableTarget.User ? userSource : furniSource;

        if (frame.ResolveSource is { } resolve) {
            return resolve(target, source, picked).Where(frame.Contains).Distinct();
        }

        var selected = picked.ToHashSet();
        var candidates = source switch
        {
            0 or 11 => frame.Trigger,
            100 or 101 => frame.Holders.Where(x => x.Target == WiredVariableTarget.Furni && selected.Contains(unchecked((uint)x.EntityId))),
            200 => frame.Selector,
            201 => frame.Signal,
            _ => []
        };

        return candidates.Where(x => x.Target == target && frame.Contains(x)).Distinct();
    }
    private static bool Quantify(IEnumerable<bool> values, int quantifier)
    {
        var outcomes = values.ToArray();

        return outcomes.Length > 0 && (quantifier == 1 ? outcomes.Any(x => x) : outcomes.All(x => x));
    }
    private static bool ValidToken(string token) => WiredVariableModule.TryDefinitionId(token, out _)
        || (token.StartsWith("internal:@", StringComparison.Ordinal) || token.StartsWith("internal:~", StringComparison.Ordinal)) && token.Length > 10;
}
