using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>Executable scalar boxes decoded from the active Octane legacy editor ABI.</summary>
public sealed class WiredVariableExecutors(WiredVariableModule variables, Func<long> nowMs)
{
    public static bool Supports(string name) => name is "wf_act_give_var" or "wf_act_remove_var" or "wf_act_change_var_val"
        or "wf_cnd_has_var" or "wf_cnd_neg_has_var" or "wf_cnd_var_val_match" or "wf_cnd_var_age_match";

    public static bool TryValidate(string name, WiredConfiguration configuration, out string error)
    {
        error = "Invalid scalar variable settings.";
        if (!Supports(name) || configuration.Version != 1 || configuration.Text.Length > 5000) return false;
        var p = configuration.IntParams;
        var count = name switch
        {
            "wf_act_give_var" => 5, "wf_act_remove_var" => 3, "wf_act_change_var_val" => 9,
            "wf_cnd_has_var" or "wf_cnd_neg_has_var" => 4, "wf_cnd_var_val_match" => 10, _ => 8
        };
        if (p.Length != count || !Enum.IsDefined((WiredVariableTarget)p[0])) return false;
        var tokens = configuration.Text.Split('\t');
        if (!ValidToken(tokens[0])) return false;
        if (name is "wf_act_give_var" or "wf_act_remove_var")
        {
            if (!WiredVariableModule.TryDefinitionId(tokens[0], out _) || p[0] == 3) return false;
            if (name == "wf_act_give_var" && p[1] is not (0 or 1)) return false;
        }
        if (name is "wf_act_change_var_val" or "wf_cnd_var_val_match")
        {
            if (name == "wf_act_change_var_val" ? !WiredVariableArithmetic.IsSupported(p[1]) : p[1] is < 0 or > 5) return false;
            if (p[2] is not (0 or 1) || !Enum.IsDefined((WiredVariableTarget)p[4])) return false;
            if (p[2] == 1 && (tokens.Length < 2 || !ValidToken(tokens[1]))) return false;
            if (tokens.Length > 2 && tokens[2].Split(';', StringSplitOptions.RemoveEmptyEntries).Any(x => !uint.TryParse(x, out var id) || id == 0)) return false;
        }
        if (name == "wf_cnd_var_age_match" && (p[1] is not (0 or 1) || p[2] is not (0 or 2) || p[3] is < 0 or > 1000000 || p[4] is < 0 or > 7)) return false;
        if (name.StartsWith("wf_cnd_", StringComparison.Ordinal) && p[^1] is not (0 or 1)) return false;
        var sourceIndices = name switch
        {
            "wf_act_give_var" => new[] { 3, 4 }, "wf_act_remove_var" => [1, 2],
            "wf_act_change_var_val" or "wf_cnd_var_val_match" => [5, 6, 7, 8],
            "wf_cnd_var_age_match" => [5, 6], _ => [1, 2]
        };
        if (sourceIndices.Any(i => p[i] is not (0 or 11 or 100 or 101 or 200 or 201))) return false;
        error = "";
        return true;
    }

    public bool Execute(string name, WiredConfiguration configuration, WiredVariableFrame frame)
    {
        if (!TryValidate(name, configuration, out _)) return false;
        var p = configuration.IntParams;
        var tokens = configuration.Text.Split('\t');
        var reference = new WiredVariableReference((WiredVariableTarget)p[0], tokens[0]);
        var sourceStart = name switch { "wf_act_give_var" => 3, "wf_act_remove_var" => 1,
            "wf_act_change_var_val" or "wf_cnd_var_val_match" or "wf_cnd_var_age_match" => 5, _ => 1 };
        var targets = Select(frame, reference.Target, p[sourceStart], p[sourceStart + 1], configuration.SelectedItems).ToArray();
        if (name is "wf_act_give_var" or "wf_act_remove_var")
        {
            var mutation = name == "wf_act_remove_var" ? WiredVariableMutation.Remove : p[1] == 1 ? WiredVariableMutation.Replace : WiredVariableMutation.Give;
            var changed = false;
            foreach (var target in targets) changed |= variables.Mutate(reference, target, mutation, name == "wf_act_remove_var" ? 0 : p[2], frame);
            return changed;
        }
        if (name is "wf_cnd_has_var" or "wf_cnd_neg_has_var")
        {
            var outcomes = targets.Select(target => variables.Read(reference, target, frame) is not null).ToArray();
            return Quantify(outcomes.Select(x => name == "wf_cnd_neg_has_var" ? !x : x), p[^1]);
        }
        if (name == "wf_cnd_var_age_match")
        {
            // Octane duration units: milliseconds, seconds, minutes, hours, days, weeks, months, years.
            long[] units = [1, 1000, 60000, 3600000, 86400000, 604800000, 2592000000, 31536000000];
            var duration = (long)p[3] * units[p[4]];
            return Quantify(targets.Select(target =>
            {
                var current = variables.Read(reference, target, frame);
                if (current is null) return false;
                var timestamp = p[1] == 1 ? current.UpdatedAtMs : current.CreatedAtMs;
                if (timestamp <= 0) return false;
                var age = Math.Max(0, nowMs() - timestamp);
                return p[2] == 0 ? age < duration : age > duration;
            }), p[^1]);
        }
        var operand = p[3];
        if (p[2] == 1 && !(name == "wf_act_change_var_val" && WiredVariableArithmetic.IsUnary(p[1])))
        {
            var operandReference = new WiredVariableReference((WiredVariableTarget)p[4], tokens[1]);
            var picked = tokens.Length > 2 ? tokens[2].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(uint.Parse) : configuration.SecondarySelectedItems;
            var found = Select(frame, operandReference.Target, p[7], p[8], picked)
                .Select(target => variables.Read(operandReference, target, frame)).FirstOrDefault(value => value is not null);
            if (found is null) return false;
            operand = found.Value;
        }
        if (name == "wf_cnd_var_val_match")
            return Quantify(targets.Select(target => variables.Read(reference, target, frame) is { } value
                && WiredVariablePredicates.Compare(p[1], value.Value, operand)), p[^1]);
        var any = false;
        foreach (var target in targets)
            any |= variables.Change(reference, target, WiredVariableMutation.Set, current => WiredVariableArithmetic.Apply(p[1], current, operand), frame);
        return any;
    }

    public static IEnumerable<WiredVariableHolder> Select(WiredVariableFrame frame, WiredVariableTarget target,
        int userSource, int furniSource, IEnumerable<uint> picked)
    {
        if (target is WiredVariableTarget.Context or WiredVariableTarget.Global)
            return [new(target, 0, 0)];
        var source = target == WiredVariableTarget.User ? userSource : furniSource;
        var selected = picked.ToHashSet();
        var candidates = source switch
        {
            0 or 11 => frame.Trigger,
            100 or 101 => frame.Holders.Where(x => x.Target == WiredVariableTarget.Furni && selected.Contains((uint)x.StableId)),
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
        || token.StartsWith("internal:@", StringComparison.Ordinal) && token.Length > 10;
}
