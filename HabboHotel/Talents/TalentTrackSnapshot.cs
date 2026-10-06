using System.Collections.Immutable;

namespace Plus.HabboHotel.Talents;

public sealed record TalentTrackSubLevelSnapshot(string Badge, int RequiredProgress);

public sealed record TalentTrackLevelSnapshot(int Level, ImmutableArray<TalentTrackSubLevelSnapshot> SubLevels, ImmutableArray<string> Actions, ImmutableArray<string> Gifts)
{
    public static TalentTrackLevelSnapshot Capture(TalentTrackLevel level) => new(
        level.Level,
        level.GetSubLevels().Select(sub => new TalentTrackSubLevelSnapshot(sub.Badge, sub.RequiredProgress)).ToImmutableArray(),
        level.Actions.ToImmutableArray(),
        level.Gifts.ToImmutableArray());
}

public static class TalentTrackSnapshot
{
    // Copies every level, sub-level, action and gift in enumeration order, so later changes to the source do not reach a packet.
    public static ImmutableArray<TalentTrackLevelSnapshot> Capture(IEnumerable<TalentTrackLevel> levels) =>
        levels.Select(TalentTrackLevelSnapshot.Capture).ToImmutableArray();
}
