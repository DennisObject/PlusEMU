namespace Plus.HabboHotel.Items.Data.Moodlight;

/// <summary>The stored sidecar row. Presets stay raw text so an untouched legacy value is never rewritten or reparsed.</summary>
public sealed record MoodlightRecord(int SidecarId, bool Enabled, int CurrentPreset, string PresetOne, string PresetTwo, string PresetThree);
