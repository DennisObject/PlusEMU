using System.Globalization;
using Plus.Core.Settings;

namespace Plus.HabboHotel.Rooms.PathFinding;

public enum PathfindingEngine
{
    Legacy, Shadow, V2
}
public enum CornerRule
{
    Official, Strict, None
}
public sealed record PathfindingSettings
{
    public PathfindingEngine Engine { get; init; } = PathfindingEngine.Legacy;
    public string Profile { get; init; } = "plus";
    public double? MaxStepUp { get; init; }
    public double? MaxStepDown { get; init; }
    public bool UnlimitedDown { get; init; }
    public double EffectiveMaxUp => MaxStepUp ?? (Profile == "habbo2013" ? 1.25 : 1.5);
    public double? EffectiveMaxDown => UnlimitedDown ? null : MaxStepDown ?? (Profile == "habbo2013" ? 4.0 : null);
    public CornerRule CornerRule { get; init; } = CornerRule.Official;
    public bool LayeringEnabled { get; init; }
    public bool StacktoolLegacyCollision { get; init; } = true;
    public double AvatarClearance { get; init; } = 1.5;
    public int MaxSurfacesPerTile { get; init; } = 2;
    public string UnreachablePolicy { get; init; } = "stay";
    public int BlockWaitTicks { get; init; } = 1;
    public int MaxBlockReplans { get; init; } = 3;
    public int MaxWalkStallTicks { get; init; } = 10;
    public bool FastwalkIntermediateHooks { get; init; }
    public bool RidersIgnoreHeight { get; init; } = true;
    public int? MaxExpansionsPerSearch { get; init; }
    public int MaxExpansionsPerRoomTick { get; init; } = 200000;
    public double ShadowLogSample { get; init; } = 0.05;
    public bool ApproachAutoInteract { get; init; } = true;

    public static PathfindingSettings Load(ISettingsManager manager)
    {
        string? Read(string key) => manager.GetOptionalValue("pathfinding." + key);
        double? Number(string key) => double.TryParse(Read(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            && double.IsFinite(n) && n >= 0 ? n : null;
        int Integer(string key, int fallback) => int.TryParse(Read(key), out var n) && n >= 0 ? n : fallback;
        bool Boolean(string key, bool fallback = false) => Read(key) is { } text ? text == "1" : fallback;

        return new()
        {
            Engine = Read("engine") switch { "shadow" => PathfindingEngine.Shadow, "v2" => PathfindingEngine.V2, _ => PathfindingEngine.Legacy },
            Profile = Read("profile") == "habbo2013" ? "habbo2013" : "plus",
            MaxStepUp = Number("max_step_up"),
            MaxStepDown = Number("max_step_down"),
            UnlimitedDown = Read("max_step_down") == "none",
            CornerRule = Read("corner_rule") switch { "none" => CornerRule.None, "strict" => CornerRule.Strict, _ => CornerRule.Official },
            LayeringEnabled = Boolean("layering_enabled"),
            StacktoolLegacyCollision = Boolean("stacktool_legacy_collision", true),
            AvatarClearance = Number("avatar_clearance") ?? 1.5,
            MaxSurfacesPerTile = Math.Clamp(Integer("max_surfaces_per_tile", 2), 1, NavGrid.MaxSurfacesPerTile),
            UnreachablePolicy = Read("unreachable_policy") == "nearest" ? "nearest" : "stay",
            BlockWaitTicks = Integer("block_wait_ticks", 1),
            MaxBlockReplans = Integer("max_block_replans", 3),
            MaxWalkStallTicks = Integer("max_walk_stall_ticks", 10),
            FastwalkIntermediateHooks = Boolean("fastwalk_intermediate_hooks"),
            RidersIgnoreHeight = Boolean("riders_ignore_height", true),
            MaxExpansionsPerSearch = int.TryParse(Read("max_expansions_per_search"), out var cap) && cap > 0 ? cap : null,
            ApproachAutoInteract = Boolean("approach_auto_interact", true),
            MaxExpansionsPerRoomTick = Integer("max_expansions_per_room_tick", 200000),
            ShadowLogSample = Math.Min(1, Number("shadow_log_sample") ?? 0.05)
        };
    }
}
