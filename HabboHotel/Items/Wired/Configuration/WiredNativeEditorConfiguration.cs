using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>The single stored authority for a canonical native editor save.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WiredNativeEditorConfiguration
{
    public int Version { get; init; } = 2;
    public WiredBoxCategory Category { get; init; }
    public int NativeCode { get; init; }
    public ImmutableArray<int> OwnedIntParams { get; init; } = [];
    public string Text { get; init; } = "";
    public ImmutableArray<WiredNativeItemReference> PrimaryItems { get; init; } = [];
    public ImmutableArray<WiredNativeItemReference> SecondaryItems { get; init; } = [];
    public ImmutableArray<int> FurniSourceTypes { get; init; } = [];
    public ImmutableArray<int> UserSourceTypes { get; init; } = [];
    public ImmutableArray<string> VariableIds { get; init; } = [];
    public int? Delay { get; init; }
    public int? Quantifier { get; init; }
    public bool? Filter { get; init; }
    public bool? Inverse { get; init; }
    public WiredNativeSavedState SavedState { get; init; } = new();
}

public sealed record WiredNativeItemReference(uint ItemId, bool Wall)
{
    [JsonIgnore]
    public int WireId => Wall ? checked(-(int)ItemId) : checked((int)ItemId);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WiredNativeSavedState
{
    public ImmutableArray<WiredFurniSnapshot> Snapshots { get; init; } = [];
}

/// <summary>The single authority behind an executable runtime: the native editor record it was compiled from.</summary>
internal sealed record WiredConfigurationOrigin(uint ItemId, string Name,
    WiredConfiguration Derived, WiredNativeEditorConfiguration Native);

public enum WiredNativeSaveAdmission
{
    Unchanged, Changed, Refused
}

public sealed record WiredNativeEditorMetadata(ImmutableArray<ImmutableArray<int>> FurniAllowed,
    ImmutableArray<ImmutableArray<int>> UsersAllowed, ImmutableArray<int> FurniDefaults,
    ImmutableArray<int> UserDefaults, ImmutableArray<int> OwnedDefaults, bool AllowWall)
{
    /// <summary>One empty slot per variable picker; an unchosen variable is the safe inactive default.</summary>
    public ImmutableArray<string> VariableDefaults { get; init; } = [];

    /// <summary>Condition footer: 0 none, 1 furni, 2 users, 3 variables (the AIR quantifier wording), and the invert flag.</summary>
    public int QuantifierType { get; init; }
    public bool Invert { get; init; }
}
