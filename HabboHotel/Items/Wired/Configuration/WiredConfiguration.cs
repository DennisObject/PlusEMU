using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>
/// Immutable editor settings and their decoded runtime inputs. Concrete boxes own the mapping
/// from Octane's per-name int/text fields to sources and variable references; there are no shared slots.
/// </summary>
public sealed record WiredConfiguration
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public ImmutableArray<int> IntParams { get; init; } = [];
    public string Text { get; init; } = string.Empty;
    public ImmutableArray<uint> SelectedItems { get; init; } = [];
    public int Delay { get; init; }
    public int SelectionCode { get; init; }
    // Server-side per-user, per-game limit. Null preserves legacy unlimited scoring; this is not an editor int slot.
    public int? ScoreQuotaPerGame { get; init; }
    public ImmutableArray<uint> SecondarySelectedItems { get; init; } = [];
    public ImmutableDictionary<string, int> FurniSources { get; init; } = ImmutableDictionary<string, int>.Empty;
    public ImmutableDictionary<string, int> UserSources { get; init; } = ImmutableDictionary<string, int>.Empty;
    public ImmutableArray<string> VariableIds { get; init; } = [];
    public ImmutableArray<WiredFurniSnapshot> Snapshots { get; init; } = [];
}
