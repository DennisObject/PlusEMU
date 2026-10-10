using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Triggers;

/// <summary>Event matching for Volt's saved trigger fields; hooks supply actual event facts.</summary>
public static class WiredTriggerPredicates
{
    public static bool MatchesName(WiredConfiguration config, string? name) =>
        string.IsNullOrWhiteSpace(config.Text)
        || string.Equals(config.Text.Trim(), name, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesChat(WiredConfiguration config, string? message, bool isOwner)
    {
        if (Param(config, 2) == 1 && !isOwner) {
            return false;
        }

        var text = message?.Trim() ?? string.Empty;
        var keyword = config.Text.Trim();

        return Param(config, 0) switch
        {
            2 => text.Length != 0,
            1 => keyword.Length != 0 && string.Equals(text, keyword, StringComparison.OrdinalIgnoreCase),
            _ => keyword.Length != 0 && text.Contains(keyword, StringComparison.OrdinalIgnoreCase)
        };
    }

    public static bool HidesChat(WiredConfiguration config) => Param(config, 1) == 1;

    public static bool MatchesScore(WiredConfiguration config, int team, int oldScore, int newScore) =>
        (Param(config, 1) == 0 || Param(config, 1) == team)
        && oldScore < Param(config, 0, 1) && newScore >= Param(config, 0, 1);

    // Action IDs are Polaris editor values, not Turbo's expression enum.
    public static bool MatchesAction(WiredConfiguration config, int action, int value)
    {
        if (Param(config, 0, 1) != action) {
            return false;
        }

        if (action == 9 && Param(config, 1) == 1) {
            return Param(config, 2) == value;
        }

        if (action == 10 && Param(config, 3) == 1) {
            return Param(config, 4) == value;
        }

        return true;
    }

    public static bool MatchesItem(WiredConfiguration config, Item eventItem,
        IEnumerable<Item> resolvedSubjects, bool supportsSavedState)
    {
        if (!resolvedSubjects.Any(item => item.Id == eventItem.Id)) {
            return false;
        }

        if (!supportsSavedState || Param(config, 0) == 0) {
            return true;
        }

        var snapshot = config.Snapshots.FirstOrDefault(entry => entry.ItemId == eventItem.Id);

        return snapshot != null && string.Equals(snapshot.State, eventItem.LegacyDataString ?? string.Empty,
            StringComparison.Ordinal);
    }

    public static bool MatchesCounter(WiredConfiguration config, long oldMs, long newMs) =>
        oldMs < CounterTargetMs(config, 0) && newMs >= CounterTargetMs(config, 0);

    public static long CounterTargetMs(WiredConfiguration config, int minutesIndex) =>
        Param(config, minutesIndex) * 60_000L + Param(config, minutesIndex + 1) * 500L;

    private static int Param(WiredConfiguration config, int index, int fallback = 0) =>
        index < config.IntParams.Length ? config.IntParams[index] : fallback;
}
