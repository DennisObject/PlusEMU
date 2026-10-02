using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Plus.HabboHotel.Items.Wired.Variables.Fx;

/// <summary>
/// Per-viewer deltas. Pass only holders whose room objects the viewer has received. Call after variable,
/// team, config, placement or builtin changes. Remove the viewer on leave; never share this state between rooms.
/// </summary>
public sealed class WiredVariableFxTracker(WiredVariableModule variables, int maxStatuses = 1000)
{
    private readonly Dictionary<long, Viewer> _viewers = [];
    private readonly Dictionary<long, (WiredVariableFxBatch Batch, Viewer State)> _pending = [];
    public void RemoveViewer(long stableUserId) { _viewers.Remove(stableUserId); _pending.Remove(stableUserId); }
    /// <summary>Call only after every packet in the batch was successfully sent. A failed send leaves the batch retryable.</summary>
    public bool Acknowledge(long stableUserId, WiredVariableFxBatch batch)
    {
        if (!_pending.TryGetValue(stableUserId, out var pending) || !ReferenceEquals(pending.Batch, batch)) return false;
        _viewers[stableUserId] = pending.State;
        _pending.Remove(stableUserId);
        return true;
    }

    public WiredVariableFxBatch Update(WiredVariableHolder viewer, WiredVariableFrame frame,
        IReadOnlyList<WiredVariableFxBinding> bindings, IReadOnlyList<WiredVariableHolder> readyHolders,
        Func<WiredVariableHolder, int> team)
    {
        if (viewer.Target != WiredVariableTarget.User || !viewer.CanPersist || !frame.Contains(viewer))
            throw new ArgumentException("FX viewer must be a present player.", nameof(viewer));
        if (!_viewers.TryGetValue(viewer.StableId, out var state)) state = new();
        var configs = bindings.ToDictionary(x => x.Config.Id);
        var changedConfigs = bindings.Where(x => !state.Configs.TryGetValue(x.Config.Id, out var old) || old != Signature(x.Config))
            .Select(x => x.Config).ToArray();
        var removedConfigs = state.Configs.Keys.Where(x => !configs.ContainsKey(x)).ToArray();
        var wanted = new Dictionary<WiredVariableFxKey, WiredVariableFxStatus>();
        foreach (var binding in bindings)
        {
            foreach (var holder in readyHolders)
            {
                if (wanted.Count >= maxStatuses) break;
                if (holder.Target != binding.Variable.Target || !frame.Contains(holder) || !CanSee(binding, viewer, holder, frame, team)) continue;
                var value = variables.Read(binding.Variable, holder, frame);
                if (value is null) continue;
                var min = ReadOverride(binding.OverrideMin, holder, frame);
                var max = ReadOverride(binding.OverrideMax, holder, frame);
                if (min is not null || max is not null)
                {
                    min ??= binding.Config.Min; max ??= binding.Config.Max;
                    if (max <= min) max = min + 1;
                }
                var extra = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
                if (binding.Config.ColorId == 1002)
                {
                    var color = team(holder) switch { 1 => "#ff0000", 2 => "#00ff00", 3 => "#0000ff", 4 => "#ffff00", _ => null };
                    if (color is not null) extra["delegated_color"] = color;
                }
                if (binding.Config.Category == 2 || binding.Config.ColorId == 1001)
                {
                    var level = binding.Level?.Invoke(value.Value) ?? new(1, 1, binding.Config.Min, binding.Config.Max, false);
                    extra["current_level"] = level.Level.ToString(CultureInfo.InvariantCulture);
                    extra["max_level"] = level.MaxLevel.ToString(CultureInfo.InvariantCulture);
                    extra["is_maxed"] = level.IsMaxed ? "true" : "false";
                    if (binding.Level is not null) { min = level.Start; max = level.Next; }
                }
                var key = new WiredVariableFxKey(binding.Config.Id, binding.Variable.Token, binding.Config.UserFx, holder.EntityId);
                wanted[key] = new(key, !state.Statuses.ContainsKey(key), value.Value, min, max, extra.ToImmutable());
            }
        }
        var removedStatuses = state.Statuses.Keys.Where(key => !wanted.ContainsKey(key)).ToArray();
        var statuses = wanted.Where(x => !state.Statuses.TryGetValue(x.Key, out var old) || old != Signature(x.Value with { Initialize = false }))
            .Select(x => x.Value).ToArray();
        var batch = new WiredVariableFxBatch(!state.Synced, changedConfigs, removedConfigs, statuses, removedStatuses);
        _pending[viewer.StableId] = (batch, new Viewer
        {
            Configs = bindings.ToDictionary(x => x.Config.Id, x => Signature(x.Config)),
            Statuses = wanted.ToDictionary(x => x.Key, x => Signature(x.Value with { Initialize = false })),
            Synced = true
        });
        return batch;
    }

    private long? ReadOverride(WiredVariableReference? reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        if (reference is null) return null;
        var target = reference.Target == WiredVariableTarget.Global ? new(WiredVariableTarget.Global, 0, 0) : holder;
        return variables.Read(reference, target, frame)?.Value;
    }
    private bool CanSee(WiredVariableFxBinding binding, WiredVariableHolder viewer, WiredVariableHolder holder,
        WiredVariableFrame frame, Func<WiredVariableHolder, int> team)
    {
        var visibility = !binding.Config.UserFx && binding.Visibility is 0 or 1 ? 2 : binding.Visibility;
        return visibility switch
        {
            0 => viewer.StableId == holder.StableId,
            1 => viewer.StableId == holder.StableId || team(holder) != 0 && team(holder) == team(viewer),
            2 => true,
            3 => binding.Audience is { } audience && variables.Read(audience, viewer, frame) is not null,
            4 => binding.Audience is { } audience && variables.Read(audience, viewer, frame)?.Value == binding.AudienceValue,
            _ => false
        };
    }
    private static string Signature<T>(T value) => JsonSerializer.Serialize(value);
    private sealed class Viewer
    {
        public bool Synced { get; set; }
        public Dictionary<int, string> Configs { get; set; } = [];
        public Dictionary<WiredVariableFxKey, string> Statuses { get; set; } = [];
    }
}
