using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Variables.Fx;

public sealed record WiredVariableFxConfig(int Id, bool UserFx, int ShowMode, int DurationMs, int Category,
    int StyleId, int ColorId, int WidthId, int RendererId, long Min, long Max, ImmutableSortedDictionary<string, string> Extra);
public sealed record WiredVariableFxBinding(WiredVariableFxConfig Config, WiredVariableReference Variable, int Visibility,
    WiredVariableReference? Audience, int AudienceValue, WiredVariableReference? OverrideMin, WiredVariableReference? OverrideMax,
    Func<int, WiredVariableLevel>? Level = null);
public readonly record struct WiredVariableFxKey(int ConfigId, string VariableId, bool UserEntity, int EntityId)
{
    public override string ToString() => $"{ConfigId}|{VariableId}|{(UserEntity ? "u" : "f")}|{EntityId}";
}
public sealed record WiredVariableFxStatus(WiredVariableFxKey Key, bool Initialize, long Value, long? Min, long? Max,
    ImmutableSortedDictionary<string, string> Extra);
public sealed record WiredVariableFxBatch(bool InitializeAll, IReadOnlyList<WiredVariableFxConfig> Configs,
    IReadOnlyList<int> RemovedConfigs, IReadOnlyList<WiredVariableFxStatus> Statuses, IReadOnlyList<WiredVariableFxKey> RemovedStatuses);
public sealed record WiredVariableLevel(int Level, int MaxLevel, long Start, long Next, bool IsMaxed);
