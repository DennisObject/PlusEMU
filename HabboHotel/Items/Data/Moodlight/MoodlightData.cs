using System.Text;

namespace Plus.HabboHotel.Items.Data.Moodlight;

public class MoodlightData
{
    public int CurrentPreset;
    public bool Enabled;
    public uint ItemId;

    public List<MoodlightPreset> Presets;

    // Pure model: the record is loaded and validated by the metadata store before any live state exists.
    public MoodlightData(uint itemId, MoodlightRecord record)
    {
        ItemId = itemId;
        Enabled = record.Enabled;
        CurrentPreset = record.CurrentPreset;
        Presets = new();
        Presets.Add(GeneratePreset(record.PresetOne));
        Presets.Add(GeneratePreset(record.PresetTwo));
        Presets.Add(GeneratePreset(record.PresetThree));
    }

    public static MoodlightPreset GeneratePreset(string data)
    {
        var bits = data.Split(',');
        if (!IsValidColor(bits[0])) bits[0] = "#000000";
        return new(bits[0], int.Parse(bits[1]), bits[2] == "1");
    }

    public MoodlightPreset GetPreset(int i)
    {
        i--;
        if (Presets[i] != null) return Presets[i];
        return new("#000000", 255, false);
    }

    public static bool IsValidColor(string colorCode)
    {
        switch (colorCode)
        {
            case "#000000":
            case "#0053F7":
            case "#EA4532":
            case "#82F349":
            case "#74F5F5":
            case "#E759DE":
            case "#F2F851":
                return true;
            default:
                return false;
        }
    }

    public static bool IsValidIntensity(int intensity)
    {
        if (intensity < 0 || intensity > 255) return false;
        return true;
    }

    public string GenerateExtraData() => Serialize(Enabled, CurrentPreset, GetPreset(CurrentPreset));

    // Serialized state for a candidate, built before any persistence so a failure cannot follow a write.
    public static string Serialize(bool enabled, int currentPreset, MoodlightPreset preset)
    {
        var sb = new StringBuilder();
        sb.Append(enabled ? 2 : 1);
        sb.Append(",");
        sb.Append(currentPreset);
        sb.Append(",");
        sb.Append(preset.BackgroundOnly ? 2 : 1);
        sb.Append(",");
        sb.Append(preset.ColorCode);
        sb.Append(",");
        sb.Append(preset.ColorIntensity);
        return sb.ToString();
    }

}
