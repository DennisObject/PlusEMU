using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Wired.Variables.Fx;

/// <summary>The variable FX style catalog the AIR editor and renderer share (categories, styles and their allowed options).</summary>
public sealed record WiredVariableFxStyle(int Category, int Id, ImmutableArray<int> Colors, ImmutableArray<int> Widths,
    ImmutableArray<int> Renderers, int DefaultColor, int DefaultWidth, int DefaultRenderer, ImmutableSortedDictionary<string, string> Extra)
{
    public ImmutableArray<int> SubRenderers => Extra.TryGetValue("sub_renderer", out var value)
        ? DefaultRenderer == 20 ? [2, 3, 4] : [int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)] : [];
}

public static class WiredVariableFxStyles
{
    private static readonly Dictionary<string, int> Colors = new()
    {
        ["NOT_APPLICABLE"] = -1, ["GREEN"] = 1, ["LIME_GREEN"] = 2, ["YELLOW"] = 3, ["ORANGE"] = 4, ["RED"] = 5, ["CYAN"] = 6, ["BLUE"] = 7,
        ["PURPLE"] = 8, ["PINK"] = 9, ["BROWN"] = 10, ["BEIGE"] = 11, ["TEAL"] = 12, ["INDIGO"] = 13, ["MAGENTA"] = 14, ["LIGHT_BLUE"] = 15,
        ["FIRE_ORANGE"] = 16, ["DARK_GREEN"] = 17, ["DARK_BLUE"] = 18, ["WHITE"] = 19, ["BRONZE"] = 100, ["SILVER"] = 101, ["GOLD"] = 102,
        ["DIAMOND"] = 103, ["EMERALD"] = 104, ["DYNAMIC_RED_TO_GREEN"] = 1000, ["DYNAMIC_LEVELLING"] = 1001, ["DYNAMIC_TEAM_COLOR"] = 1002
    };
    private static readonly Dictionary<string, int> Widths = new()
    {
        ["not_applicable"] = -1, ["extra_small"] = 0, ["small"] = 1, ["medium"] = 2, ["large"] = 3, ["extra_large"] = 4, ["big_mahoosive_chonky"] = 100
    };
    private static readonly Dictionary<string, int> Renderers = new()
    {
        ["classic_progress"] = 0, ["classic_mini_progress"] = 1, ["block_progress"] = 2, ["striped_progress"] = 3, ["arrow_progress"] = 4,
        ["health_progress"] = 10, ["masked_heart_fill"] = 11, ["stacked_health_points"] = 12, ["thermometer_health_points"] = 13,
        ["level_with_progress"] = 20, ["level_with_bar_and_numerical_progress"] = 21, ["boss_health_bar"] = 100, ["numerical_progress"] = 101,
        ["number_recolorable"] = 200, ["number_baked_colors"] = 201
    };
    private static readonly string[] Normal = ["GREEN", "LIME_GREEN", "YELLOW", "ORANGE", "RED", "CYAN", "BLUE", "PURPLE", "PINK", "BROWN", "BEIGE",
        "TEAL", "INDIGO", "MAGENTA", "LIGHT_BLUE", "FIRE_ORANGE", "DARK_GREEN", "DARK_BLUE", "WHITE", "BRONZE", "SILVER", "GOLD", "DIAMOND", "EMERALD"];
    private static readonly string[] Status = ["block_progress", "striped_progress", "arrow_progress"];
    private static readonly string[] XsToXl = ["extra_small", "small", "medium", "large", "extra_large"];
    private static readonly string[] SmallToLarge = ["small", "medium", "large"];
    private static readonly string[] SToXl = ["small", "medium", "large", "extra_large"];
    private static readonly string[] LToXxl = ["large", "extra_large", "big_mahoosive_chonky"];

    /// <summary>Every style by (category, style id).</summary>
    public static IReadOnlyDictionary<(int Category, int Id), WiredVariableFxStyle> All { get; } = Build();

    public static bool TryGet(int category, int style, out WiredVariableFxStyle definition) => All.TryGetValue((category, style), out definition!);

    private static IReadOnlyDictionary<(int, int), WiredVariableFxStyle> Build()
    {
        var all = new Dictionary<(int, int), WiredVariableFxStyle>();
        string[] team = [.. Normal, "DYNAMIC_TEAM_COLOR"];
        string[] redToGreenTeam = [.. Normal, "DYNAMIC_RED_TO_GREEN", "DYNAMIC_TEAM_COLOR"];
        string[] levelling = [.. Normal, "DYNAMIC_LEVELLING", "DYNAMIC_TEAM_COLOR"];

        void Add(string[] colors, string[] renderers, string[] widths, int category, int id, string color, string renderer, string width,
            params (string Key, string Value)[] extra) => all[(category, id)] = new(category, id, [.. colors.Select(name => Colors[name])],
            [.. widths.Select(name => Widths[name])], [.. renderers.Select(name => Renderers[name])], Colors[color], Widths[width], Renderers[renderer],
            extra.ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

        Add(team, ["classic_progress"], XsToXl, 1, 0, "GREEN", "classic_progress", "medium");
        Add(team, ["block_progress"], XsToXl, 1, 1, "GREEN", "block_progress", "medium");
        Add(team, ["striped_progress"], XsToXl, 1, 2, "GREEN", "striped_progress", "medium");
        Add(team, ["arrow_progress"], XsToXl, 1, 3, "GREEN", "arrow_progress", "medium");
        Add(team, ["classic_mini_progress"], XsToXl, 1, 4, "GREEN", "classic_mini_progress", "medium");
        Add(["DYNAMIC_RED_TO_GREEN"], ["health_progress"], SmallToLarge, 0, 0, "DYNAMIC_RED_TO_GREEN", "health_progress", "medium", ("icon", "misc_heart"));
        Add(team, ["masked_heart_fill"], ["not_applicable"], 0, 3, "RED", "masked_heart_fill", "not_applicable");
        Add(team, ["stacked_health_points"], ["medium", "large"], 0, 1, "RED", "stacked_health_points", "medium");
        Add(["NOT_APPLICABLE"], ["thermometer_health_points"], XsToXl, 0, 2, "NOT_APPLICABLE", "thermometer_health_points", "medium");
        Add(levelling, ["level_with_progress"], SmallToLarge, 2, 0, "DYNAMIC_LEVELLING", "level_with_progress", "medium", ("sub_renderer", "2"));
        Add(levelling, ["level_with_bar_and_numerical_progress"], SToXl, 2, 1, "DYNAMIC_LEVELLING", "level_with_bar_and_numerical_progress", "medium", ("sub_renderer", "1"));
        Add(["RED"], ["boss_health_bar"], LToXxl, 4, 0, "RED", "boss_health_bar", "extra_large", ("icon", "misc_skull"), ("icon_alignment", "double"));
        Add(redToGreenTeam, ["boss_health_bar"], LToXxl, 4, 1, "RED", "boss_health_bar", "extra_large");
        Add(["RED", "GREEN", "BLUE", "YELLOW", "WHITE", "DYNAMIC_TEAM_COLOR"], ["number_baked_colors"], ["not_applicable"], 5, 0, "GREEN", "number_baked_colors", "not_applicable", ("design", "freeze_style"));
        Add(team, ["number_recolorable"], ["not_applicable"], 5, 1, "GREEN", "number_recolorable", "not_applicable", ("design", "shalimar"));
        Add(team, ["number_recolorable"], ["not_applicable"], 5, 2, "GREEN", "number_recolorable", "not_applicable", ("design", "blocky"));

        void Icon(int id, string icon, string color, bool metallic = false) => Add(["NOT_APPLICABLE"], Status, XsToXl, 3, id, "NOT_APPLICABLE",
            "block_progress", "medium", ("icon", icon), ("color", color), ("metallic", metallic ? "true" : "false"));

        Icon(0, "energy", "#ffd83d");
        Icon(1, "shield", "#4aa9f6");
        Icon(2, "magic", "#8751d1");
        Icon(3, "food", "#ff9f24");
        Icon(4, "stamina", "#86d213");
        Icon(5, "poison", "#8ddc35");
        Icon(6, "mana", "#268fff");
        Icon(7, "health", "#7dce35");
        Icon(8, "gold", "#ffc83d", true);
        Icon(9, "gems", "#416bdd", true);
        Icon(10, "honor", "#fac384");
        Icon(11, "reputation", "#ffd83d");
        Icon(12, "cooldown", "#b8c3cc");
        Icon(13, "timeleft", "#74b9e8");
        Icon(14, "burning", "#ff5a1f");
        Icon(15, "freezing", "#82cfff");
        Add(["DYNAMIC_RED_TO_GREEN"], Status, XsToXl, 3, 16, "DYNAMIC_RED_TO_GREEN", "block_progress", "medium", ("icon", "battery"));
        Icon(17, "repairing", "#c9c5b8", true);
        Icon(18, "stealth", "#6254a8");
        Icon(19, "upgrading", "#6bdc34");
        Icon(20, "star_power", "#ffd900", true);
        Icon(21, "droplet", "#4aabf5");

        return all;
    }
}
