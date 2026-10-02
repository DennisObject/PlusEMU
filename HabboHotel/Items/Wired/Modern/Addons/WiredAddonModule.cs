using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;

namespace Plus.HabboHotel.Items.Wired.Modern.Addons;

public sealed record WiredAddonVariableRequest(int Target, string Token, int UserSource, int FurniSource,
    WiredConfiguration Configuration);
public sealed record WiredAddonInputs(WiredSelectorWorld World, WiredSelectorInputs Selection, long NowMs,
    Func<WiredAddonVariableRequest, long?>? ReadVariable = null);

/// <summary>One instance per placed addon; Reset is required on configure, move and pickup.</summary>
public sealed class WiredAddonModule
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "wf_xtra_anim_time", "wf_xtra_mov_no_animation", "wf_xtra_mov_carry_users", "wf_xtra_or_eval",
        "wf_xtra_exec_in_order", "wf_xtra_execution_limit", "wf_xtra_text_output_furni_name",
        "wf_xtra_filter_furni", "wf_xtra_mov_curve", "wf_xtra_mov_physics", "wf_xtra_rotate_to_dir",
        "wf_xtra_random", "wf_xtra_unseen", "wf_xtra_filter_users", "wf_xtra_text_output_username"
    ];
    private readonly Queue<long> _executions = new();
    private readonly Random _random;
    private IWiredActionPicker? _picker;
    private long _lastNow;
    public string Name { get; }
    public WiredConfiguration Configuration { get; private set; }

    public WiredAddonModule(string name, WiredConfiguration configuration, Random? random = null)
    {
        Name = name;
        _random = random ?? Random.Shared;
        Configuration = WiredAddonConfiguration.Normalize(name, configuration);
        CreatePicker();
    }

    public void Configure(WiredConfiguration configuration)
    {
        Configuration = WiredAddonConfiguration.Normalize(Name, configuration);
        Reset();
        CreatePicker();
    }

    public void Reset()
    {
        _executions.Clear();
        _lastNow = 0;
        _picker?.Reset();
    }

    public bool Apply(WiredAddonInputs input, WiredAddonPolicy policy, WiredConfiguration? snapshot = null)
    {
        var c = snapshot is null ? Configuration : WiredAddonConfiguration.Normalize(Name, snapshot);
        int P(int index) => WiredSelectorSources.Param(c, index);
        HashSet<uint> Furni(int source) => WiredSelectorSources.Furni(source, c, input.Selection, input.World).ToHashSet();
        HashSet<int> Users(int source) => WiredSelectorSources.Users(source, input.Selection, input.World).ToHashSet();
        switch (Name)
        {
            case "wf_xtra_anim_time": policy.AnimationTimeMs = P(0); break;
            case "wf_xtra_mov_no_animation": policy.DisableAnimation = true; break;
            case "wf_xtra_mov_carry_users": policy.Carry = new(P(0) == 1, Users(P(1))); break;
            case "wf_xtra_mov_physics":
                policy.Physics = new(P(0) == 1, P(1) == 1 ? Furni(P(4)) : [],
                    P(2) == 1 ? Users(P(6)) : [], P(3) == 1 ? Furni(P(5)) : []);
                break;
            case "wf_xtra_or_eval":
                policy.Conditions = new((WiredConditionEvaluation)P(0), P(1), P(2), Furni(P(1))); break;
            case "wf_xtra_exec_in_order": policy.ExecuteInOrder = true; break;
            case "wf_xtra_execution_limit": return Acquire(input.NowMs, P(0), P(1));
            case "wf_xtra_random":
            case "wf_xtra_unseen": policy.ActionPicker = _picker; break;
            case "wf_xtra_filter_furni":
                if (P(0) > 0) policy.FurniLimit = Math.Min(policy.FurniLimit ?? int.MaxValue, P(0));
                break;
            case "wf_xtra_filter_users":
                if (P(0) > 0) policy.UserLimit = Math.Min(policy.UserLimit ?? int.MaxValue, P(0));
                break;
            case "wf_xtra_text_output_furni_name":
            case "wf_xtra_text_output_username":
            {
                var parts = c.Text.Split('\t', 2);
                if (parts[0].Length == 0) break;
                var token = "$(" + parts[0] + ")";
                policy.TextFormatters.Add((current, text) =>
                {
                    IEnumerable<string> names;
                    if (Name == "wf_xtra_text_output_furni_name")
                    {
                        var ids = WiredSelectorSources.Furni(P(1), c, current.Selection, current.World).ToHashSet();
                        names = current.World.Furni.Where(x => ids.Contains(x.Id)).Select(x => x.Name);
                    }
                    else
                    {
                        var ids = WiredSelectorSources.Users(P(1), current.Selection, current.World).ToHashSet();
                        names = current.World.Users.Where(x => ids.Contains(x.Id)).Select(x => x.Name);
                    }
                    var value = P(0) == 2 ? string.Join(parts[1], names) : names.FirstOrDefault() ?? "";
                    return text.Replace(token, value, StringComparison.Ordinal);
                });
                break;
            }
            case "wf_xtra_mov_curve":
            {
                long? strength = P(3) == 1 ? ReadVariable(input, P(4), c.Text, P(5), P(6), c) : P(2);
                if (strength is not null) policy.Curve = new(P(0), P(1), (int)Math.Clamp(strength.Value, -1000, 1000));
                break;
            }
            case "wf_xtra_rotate_to_dir":
            {
                var distance = (WiredProjectileDistance)P(14);
                long? tiles = P(15) == 1 ? ReadVariable(input, P(17), c.Text.Split('\t').ElementAtOrDefault(1) ?? "", P(21), P(22), c) : P(16);
                if (tiles is null) distance = WiredProjectileDistance.Normal;
                policy.Projectile = new(c.SelectedItems.ToHashSet(), P(0) == 1 ? P(1) : null, P(10),
                    P(18) == 0 ? null : P(18), distance, (int)Math.Clamp(tiles ?? 0, -64, 64));
                break;
            }
            default: throw new InvalidOperationException("Unsupported addon passed normalization");
        }
        return true;
    }

    private static long? ReadVariable(WiredAddonInputs input, int target, string token, int users, int furni,
        WiredConfiguration c)
    {
        if (input.ReadVariable is null) throw new InvalidOperationException("Variable operand reader is required");
        return input.ReadVariable(new(target, token, users, furni, c));
    }

    private bool Acquire(long now, int max, int window)
    {
        if (now < _lastNow) _executions.Clear();
        _lastNow = now;
        while (_executions.TryPeek(out var oldest) && now - oldest >= window) _executions.Dequeue();
        if (_executions.Count >= max) return false;
        _executions.Enqueue(now);
        return true;
    }

    private void CreatePicker() => _picker = Name switch
    {
        "wf_xtra_random" => new WiredRandomActionPicker(Configuration.IntParams[0], Configuration.IntParams[1], _random),
        "wf_xtra_unseen" => new WiredUnseenActionPicker(),
        _ => null
    };
}
