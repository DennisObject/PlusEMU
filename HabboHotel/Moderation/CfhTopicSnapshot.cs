using System.Collections.Immutable;

namespace Plus.HabboHotel.Moderation;

public readonly record struct CfhTopicSnapshot(string Caption, int Id, string Type);
public sealed record CfhTopicCategorySnapshot(string Name, ImmutableArray<CfhTopicSnapshot> Topics)
{
    public static ImmutableArray<CfhTopicCategorySnapshot> Capture(Dictionary<string, List<ModerationPresetActions>> categories) =>
        categories.Select(category => new CfhTopicCategorySnapshot(category.Key,
            category.Value.Select(topic => new CfhTopicSnapshot(topic.Caption, topic.Id, topic.Type)).ToImmutableArray()))
        .ToImmutableArray();
}
