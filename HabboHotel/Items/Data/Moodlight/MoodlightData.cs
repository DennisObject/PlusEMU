using Dapper;
using System.Text;

namespace Plus.HabboHotel.Items.Data.Moodlight;

public class MoodlightData
{
    public int CurrentPreset;
    public bool Enabled;
    public uint ItemId;

    public List<MoodlightPreset> Presets;

    public MoodlightData(uint itemId)
    {
        ItemId = itemId;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        var row = connection.QuerySingleOrDefault<MoodlightRow>(
            "SELECT enabled,current_preset AS CurrentPreset,preset_one AS PresetOne,preset_two AS PresetTwo,preset_three AS PresetThree FROM room_items_moodlight WHERE item_id=@itemId LIMIT 1",
            new { itemId });
        if (row == null)
        {
            const string preset = "#000000,255,0";
            connection.Execute(
                "INSERT INTO room_items_moodlight (item_id,enabled,current_preset,preset_one,preset_two,preset_three) VALUES (@itemId,FALSE,1,@preset,@preset,@preset)",
                new { itemId, preset });
            row = new(false, 1, preset, preset, preset);
        }
        Enabled = row.Enabled;
        CurrentPreset = row.CurrentPreset;
        Presets = new();
        Presets.Add(GeneratePreset(row.PresetOne));
        Presets.Add(GeneratePreset(row.PresetTwo));
        Presets.Add(GeneratePreset(row.PresetThree));
    }

    public void Enable()
    {
        Enabled = true;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("UPDATE room_items_moodlight SET enabled=TRUE WHERE item_id=@itemId LIMIT 1", new { itemId = ItemId });
    }

    public void Disable()
    {
        Enabled = false;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute("UPDATE room_items_moodlight SET enabled=FALSE WHERE item_id=@itemId LIMIT 1", new { itemId = ItemId });
    }

    public void UpdatePreset(int preset, string color, int intensity, bool bgOnly, bool hax = false)
    {
        if (!IsValidColor(color) || !IsValidIntensity(intensity) && !hax) return;
        string pr;
        switch (preset)
        {
            case 3:
                pr = "three";
                break;
            case 2:
                pr = "two";
                break;
            case 1:
            default:
                pr = "one";
                break;
        }
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        connection.Execute($"UPDATE room_items_moodlight SET preset_{pr}=@preset WHERE item_id=@itemId LIMIT 1",
            new { preset = $"{color},{intensity},{PlusEnvironment.BoolToEnum(bgOnly)}", itemId = ItemId });
        GetPreset(preset).ColorCode = color;
        GetPreset(preset).ColorIntensity = intensity;
        GetPreset(preset).BackgroundOnly = bgOnly;
    }

    public static MoodlightPreset GeneratePreset(string data)
    {
        var bits = data.Split(',');
        if (!IsValidColor(bits[0])) bits[0] = "#000000";
        return new(bits[0], int.Parse(bits[1]), PlusEnvironment.EnumToBool(bits[2]));
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

    public string GenerateExtraData()
    {
        var preset = GetPreset(CurrentPreset);
        var sb = new StringBuilder();
        sb.Append(Enabled ? 2 : 1);
        sb.Append(",");
        sb.Append(CurrentPreset);
        sb.Append(",");
        sb.Append(preset.BackgroundOnly ? 2 : 1);
        sb.Append(",");
        sb.Append(preset.ColorCode);
        sb.Append(",");
        sb.Append(preset.ColorIntensity);
        return sb.ToString();
    }

    private sealed record MoodlightRow(bool Enabled, int CurrentPreset, string PresetOne, string PresetTwo, string PresetThree);
}
