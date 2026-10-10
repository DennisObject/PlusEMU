namespace Plus.HabboHotel.Items.Wired.Variables;

public static class WiredVariablePredicates
{
    // Polaris/Volt comparison codes.
    public static bool Compare(int comparison, long value, long reference) => comparison switch
    {
        0 => value > reference,
        1 => value >= reference,
        2 => value == reference,
        3 => value <= reference,
        4 => value < reference,
        5 => value != reference,
        _ => false
    };

    public static IReadOnlyList<WiredVariableHolder> Filter(WiredVariableModule variables, WiredVariableReference reference,
        IEnumerable<WiredVariableHolder> holders, WiredVariableFrame frame, int sort, int count)
    {
        if (sort is < 0 or > 5) {
            return [];
        }

        using var reads = variables.CaptureReads([reference], frame);
        var values = holders.Select(holder => (holder, value: reads.Read(reference, holder, frame)))
            .Where(x => x.value is not null)
            .Select(x => (x.holder, key: sort < 2 ? x.value!.Value : sort < 4
                ? x.value!.CreatedAt?.UtcTicks ?? DateTimeOffset.UnixEpoch.UtcTicks
                : x.value!.UpdatedAt?.UtcTicks ?? DateTimeOffset.UnixEpoch.UtcTicks));
        // Polaris puts highest value first at 0, but oldest timestamp first at 2 and 4.
        var ordered = sort is 0 or 3 or 5 ? values.OrderByDescending(x => x.key) : values.OrderBy(x => x.key);

        return ordered.ThenBy(x => x.holder.EntityId).Take(Math.Clamp(count, 0, 10000)).Select(x => x.holder).ToArray();
    }
}
