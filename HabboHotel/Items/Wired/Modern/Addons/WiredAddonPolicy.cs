using Plus.HabboHotel.Items.Wired.Modern.Selectors;

namespace Plus.HabboHotel.Items.Wired.Modern.Addons;

public enum WiredConditionEvaluation
{
    All, Any, SomeButNotAll, None, LessThan, Exactly, MoreThan
}
public sealed record WiredConditionPolicy(WiredConditionEvaluation Mode, int Source, int Count,
    IReadOnlySet<uint> ConditionIds);

public interface IWiredActionPicker
{
    IReadOnlyList<uint> Pick(IReadOnlyList<uint> actionIds);
    void Reset();
}

public sealed record WiredCarryPolicy(bool SameTile, IReadOnlySet<int> UserIds);
public sealed record WiredPhysicsPolicy(bool KeepAltitude, IReadOnlySet<uint> ThroughFurni,
    IReadOnlySet<int> ThroughUsers, IReadOnlySet<uint> BlockingFurni);
public sealed record WiredCurvePolicy(int Type, int Intensity, int Strength);
public enum WiredProjectileDistance
{
    Normal, Overshoot, Fixed
}
public sealed record WiredProjectilePolicy(IReadOnlySet<uint> ItemIds, int? DirectionSystem,
    int RotationOffset, int? CurveStrength, WiredProjectileDistance Distance, int DistanceTiles);

/// <summary>One firing's typed options. Stateful pickers belong to the placed addon, not this policy.</summary>
public sealed class WiredAddonPolicy
{
    public WiredConditionPolicy? Conditions
    {
        get; set;
    }
    public IWiredActionPicker? ActionPicker
    {
        get; set;
    }
    public bool ExecuteInOrder
    {
        get; set;
    }
    public bool DisableAnimation
    {
        get; set;
    }
    public int AnimationTimeMs { get; set; } = 500;
    public int? FurniLimit
    {
        get; set;
    }
    public int? UserLimit
    {
        get; set;
    }
    public WiredCarryPolicy? Carry
    {
        get; set;
    }
    public WiredPhysicsPolicy? Physics
    {
        get; set;
    }
    public WiredCurvePolicy? Curve
    {
        get; set;
    }
    public WiredProjectilePolicy? Projectile
    {
        get; set;
    }
    public IList<Func<WiredAddonInputs, string, string>> TextFormatters { get; } = new List<Func<WiredAddonInputs, string, string>>();

    public string FormatText(WiredAddonInputs input, string text)
    {
        foreach (var formatter in TextFormatters)
        {
            text = formatter(input, text);
        }

        return text;
    }

    public WiredSelectedIds FilterSelection(WiredSelectedIds selection, Random random)
    {
        var result = selection.Copy();
        Limit(result.FurniIds, FurniLimit, random);
        Limit(result.UserIds, UserLimit, random);

        return result;
    }

    private static void Limit<T>(HashSet<T> values, int? limit, Random random)
    {
        if (limit is not > 0 || values.Count <= limit.Value)
        {
            return;
        }

        var candidates = values.ToArray();
        random.Shuffle(candidates);
        values.Clear();
        values.UnionWith(candidates.Take(limit.Value));
    }
}

/// <summary>Counts logical OR groups as one requirement; exception handling stays with the engine.</summary>
public static class WiredConditionPolicyEvaluator
{
    public static bool Matches(WiredConditionEvaluation mode, int matched, int total, int count)
    {
        if (total <= 0)
        {
            return true;
        }

        count = Math.Clamp(count, mode == WiredConditionEvaluation.LessThan ? 1 : 0, 100);

        return mode switch
        {
            WiredConditionEvaluation.All => matched >= total,
            WiredConditionEvaluation.Any => matched > 0,
            WiredConditionEvaluation.SomeButNotAll => matched > 0 && matched < total,
            WiredConditionEvaluation.None => matched == 0,
            WiredConditionEvaluation.LessThan => matched < count,
            WiredConditionEvaluation.Exactly => matched == count,
            WiredConditionEvaluation.MoreThan => matched > count,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
}
