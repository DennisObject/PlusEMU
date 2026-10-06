using System.Collections.Immutable;

namespace Plus.HabboHotel.Items.Data.Moodlight;

public sealed record MoodlightPresetSnapshot(bool BackgroundOnly, string ColorCode, int ColorIntensity);

public sealed record MoodlightConfigSnapshot(int CurrentPreset, ImmutableArray<MoodlightPresetSnapshot> Presets)
{
    public static MoodlightConfigSnapshot Capture(MoodlightData data) => new(data.CurrentPreset,
        data.Presets.Select(preset => new MoodlightPresetSnapshot(preset.BackgroundOnly, preset.ColorCode, preset.ColorIntensity)).ToImmutableArray());
}
