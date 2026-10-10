using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

/// <summary>Polaris source identifiers. Neighborhood and type selectors decode their own local enums first.</summary>
public static class WiredSelectorSources
{
    public static IEnumerable<uint> Furni(int source, WiredConfiguration configuration, WiredSelectorInputs input,
        WiredSelectorWorld world) => source switch
        {
            0 => input.Triggering.FurniIds,
            100 => configuration.SelectedItems,
            101 => configuration.SecondarySelectedItems,
            200 => input.SelectorPool.FurniIds,
            201 => input.Signal.FurniIds,
            900 => world.Furni.Select(x => x.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown furniture source")
        };

    public static IEnumerable<int> Users(int source, WiredSelectorInputs input, WiredSelectorWorld world) => source switch
    {
        0 => input.Triggering.UserIds,
        200 => input.SelectorPool.UserIds,
        201 => input.Signal.UserIds,
        11 => input.ClickedUserId is int id ? [id] : [],
        10 => input.ReachedUserId is int reachedId ? [reachedId] : [],
        900 => world.Users.Select(x => x.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown avatar source")
    };

    public static int Param(WiredConfiguration configuration, int index, int fallback = 0) =>
        index < configuration.IntParams.Length ? configuration.IntParams[index] : fallback;
}
