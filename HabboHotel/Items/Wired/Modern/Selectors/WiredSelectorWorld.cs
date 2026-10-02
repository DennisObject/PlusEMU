using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

public enum WiredSelectorEntityKind { Player = 1, Pet = 2, Bot = 4 }

public sealed record WiredSelectorFurniture(uint Id, int DefinitionId, string Name, string State,
    int X, int Y, double Z, double Height, IReadOnlyList<(int X, int Y)> Tiles, bool IsFloor = true,
    bool IsWired = false);

public sealed record WiredSelectorAvatar(int Id, string Name, WiredSelectorEntityKind Kind, int X, int Y,
    int Team = 0, IReadOnlySet<int>? GroupIds = null, int HandItem = 0, bool Sitting = false,
    bool Lying = false, bool Idle = false, int? Sign = null, int Dance = 0,
    int? LastAction = null, int LastActionParameter = 0, long LastActionAtMs = 0);

public sealed record WiredRemoteSelector(string Name, WiredConfiguration Configuration);

/// <summary>A single room read supplies every selector; IDs remain furniture IDs and avatar room indexes.</summary>
public sealed record WiredSelectorWorld(int Width, int Height, IReadOnlyList<WiredSelectorFurniture> Furni,
    IReadOnlyList<WiredSelectorAvatar> Users, int RoomGroupId = 0,
    IReadOnlyDictionary<uint, WiredRemoteSelector>? RemoteSelectors = null, bool IncludeWired = false);

public sealed class WiredSelectedIds
{
    public HashSet<uint> FurniIds { get; } = [];
    public HashSet<int> UserIds { get; } = [];

    public WiredSelectedIds Copy()
    {
        var copy = new WiredSelectedIds();
        copy.FurniIds.UnionWith(FurniIds);
        copy.UserIds.UnionWith(UserIds);
        return copy;
    }
}

public sealed record WiredSelectorInputs(WiredSelectedIds Triggering, WiredSelectedIds SelectorPool,
    WiredSelectedIds Signal, int? ClickedUserId = null, long NowMs = 0,
    int? ActionUserId = null, int? Action = null, int ActionParameter = 0,
    Func<string, WiredConfiguration, uint, bool>? FurniVariablePredicate = null,
    Func<string, WiredConfiguration, int, bool>? UserVariablePredicate = null,
    bool FurniModified = false, bool UsersModified = false, bool IncludeWired = false);

public enum WiredSelectorTarget { Furni, User, Both }

public sealed record WiredSelectorResult(WiredSelectedIds Selection, WiredSelectorTarget Target,
    bool FiltersExisting, bool Invert);
