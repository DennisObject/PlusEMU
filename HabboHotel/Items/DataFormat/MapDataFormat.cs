using System.Text;
using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Items.DataFormat;

public class MapDataFormat : FurniObjectData
{
    private Dictionary<string, string> _data = new();

    public Dictionary<string, string> Data
    {
        get => _data;
        set
        {
            _data = value;
            RaiseDataUpdated();
        }
    }

    public override FurniDataStructure StructureType => FurniDataStructure.Map;

    public MapDataFormat()
    {
    }

    public MapDataFormat(Dictionary<string, string> data) => Data = data;

    public override string Serialize()
    {
        var stringBuilder = new StringBuilder();

        foreach (var entry in Data) {
            stringBuilder.Append(Regex.Escape(entry.Key));
            stringBuilder.Append("\t");
            stringBuilder.Append(Regex.Escape(entry.Value));
            stringBuilder.Append("\n");
        }

        var result = stringBuilder.ToString().Trim();

        return result;
    }

    public override void Store(string data)
    {
        Data.Clear();

        foreach (var entry in data.Split("\n")) {
            var keyValue = entry.Split("\t");
            var key = Regex.Unescape(keyValue[0]);
            var value = string.Empty;

            if (keyValue.Length == 2) {
                value = Regex.Unescape(keyValue[1]);
            }

            Data[key] = value;
        }
    }

    public void Set(string key, string value)
    {
        Data[key] = value;
        RaiseDataUpdated();
    }
}
