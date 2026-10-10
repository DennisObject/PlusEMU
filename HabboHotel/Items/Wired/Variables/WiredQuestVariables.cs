using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Items.Wired.Variables;

public enum WiredQuestKind
{
    Quest,
    Chain
}

/// <summary>A quest definition's meaning: which real quest (its Quest.Name) or chain (its Quest.Category) it reads for each player.</summary>
public sealed record WiredQuestBinding(WiredQuestKind Kind, string Target);

/// <summary>
/// Local name/value policy over the real quest tables, not official server parity. A quest variable reads the player's own
/// progress on the one quest with that exact name; a chain variable counts the player's completed steps of the chain with
/// that exact category. A name that matches no quest, or more than one, is unavailable — never the first match or a zero.
/// </summary>
public static class WiredQuestVariables
{
    private const string Prefix = "@quest.";

    public static readonly string[] QuestKeys = ["progress", "target", "is_complete", "percent", "remaining"];
    public static readonly string[] ChainKeys = ["current_step", "total_steps", "is_complete", "percent"];

    public static string[] Keys(WiredQuestKind kind) => kind == WiredQuestKind.Quest ? QuestKeys : ChainKeys;

    public const string CurrentStepPart = "current_step";

    /// <summary>
    /// The variable's own builtin token (or, for a chain's current step, its holder-aware part); it is only meaningful for a quest
    /// definition present in this room.
    /// </summary>
    public static string Token(uint definitionId, string? part = null) => "internal:" + Prefix + definitionId + (part is null ? "" : "." + part);

    public static bool IsToken(string token) => token.StartsWith("internal:" + Prefix, StringComparison.Ordinal);

    public static bool TryParseKey(string key, out uint definitionId) => TryParseKey(key, out definitionId, out var part) && part is null;

    public static bool TryParseKey(string key, out uint definitionId, out string? part)
    {
        definitionId = 0;
        part = null;

        if (!key.StartsWith(Prefix, StringComparison.Ordinal)) {
            return false;
        }

        var rest = key[Prefix.Length..];
        var dot = rest.IndexOf('.');

        if (dot >= 0) {
            part = rest[(dot + 1)..];
            rest = rest[..dot];

            if (part != CurrentStepPart) {
                return false;
            }
        }

        return uint.TryParse(rest, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out definitionId) && definitionId > 0;
    }

    /// <summary>The binding stored in a definition's text ("variable name \t quest or chain name"), or none when blank or malformed.</summary>
    public static WiredQuestBinding? Binding(string name, string text)
    {
        var parts = text.Split('\t');

        return parts.Length == 2 && parts[1].Length is >= 1 and <= 500 && parts[1].IndexOfAny(['\r', '\n']) < 0
            ? new(name == "wf_var_quest_chain" ? WiredQuestKind.Chain : WiredQuestKind.Quest, parts[1]) : null;
    }

    public static Quest? ResolveQuest(IQuestManager quests, string name)
    {
        var matches = quests.GetQuests().Where(quest => quest.Name == name).Take(2).ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>The chain's steps in order; a category without steps, or with two steps at one position, is unavailable.</summary>
    public static IReadOnlyList<Quest>? ResolveChain(IQuestManager quests, string category)
    {
        var steps = quests.GetQuests().Where(quest => quest.Category == category).OrderBy(quest => quest.Number).ToArray();

        return steps.Length == 0 || steps.Select(step => step.Number).Distinct().Count() != steps.Length ? null : steps;
    }

    /// <summary>ExploreFindItem completes at one regardless of its stored goal data.</summary>
    public static int EffectiveTarget(Quest quest) => quest.GoalType == QuestType.ExploreFindItem ? 1 : quest.GoalData;

    /// <summary>The player's own value: quest progress, or the number of completed chain steps. Null when unavailable.</summary>
    public static long? Read(IQuestManager quests, WiredQuestBinding binding, Habbo? habbo)
    {
        if (habbo is null) {
            return null;
        }

        if (binding.Kind == WiredQuestKind.Quest) {
            return ResolveQuest(quests, binding.Target) is { } quest ? habbo.GetQuestProgress(quest.Id) : null;
        }

        return ResolveChain(quests, binding.Target) is { } steps ? steps.Count(step => step.IsCompleted(habbo.GetQuestProgress(step.Id))) : null;
    }

    /// <summary>The Number of the chain's first step this player has not completed, or its last step once all are done.</summary>
    public static long? CurrentStep(IQuestManager quests, WiredQuestBinding binding, Habbo? habbo)
    {
        if (habbo is null || binding.Kind != WiredQuestKind.Chain || ResolveChain(quests, binding.Target) is not { } steps) {
            return null;
        }

        return (steps.FirstOrDefault(step => !step.IsCompleted(habbo.GetQuestProgress(step.Id))) ?? steps[^1]).Number;
    }

    /// <summary>A derived key's value from the variable's own value and the quest tables read now; null when the quest is unavailable.</summary>
    public static long? Derive(IQuestManager quests, WiredQuestBinding binding, long value, int sub)
    {
        var progress = Math.Max(0, value);

        if (binding.Kind == WiredQuestKind.Quest) {
            if (ResolveQuest(quests, binding.Target) is not { } quest) {
                return null;
            }

            var target = EffectiveTarget(quest);

            return sub switch
            {
                0 => progress,
                1 => target,
                2 => quest.IsCompleted((int)Math.Min(progress, int.MaxValue)) ? 1 : 0,
                3 => target <= 0 ? 100 : (long)Math.Min(100m, (decimal)progress * 100 / target),
                4 => Math.Max(0, target - progress),
                _ => null
            };
        }

        if (ResolveChain(quests, binding.Target) is not { } steps) {
            return null;
        }

        // The current step needs the player's per-step progress, so it is read through its own holder-aware token instead.
        return sub switch
        {
            1 => steps.Count,
            2 => progress >= steps.Count ? 1 : 0,
            3 => (long)Math.Min(100m, (decimal)progress * 100 / steps.Count),
            _ => null
        };
    }
}
