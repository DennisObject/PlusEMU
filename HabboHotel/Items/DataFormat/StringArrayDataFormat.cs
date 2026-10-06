using System.Text;
using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Items.DataFormat;

public class StringArrayDataFormat : FurniObjectData
{
    public List<string> Data = new();

    public override FurniDataStructure StructureType => FurniDataStructure.StringArray;

    public override string Serialize()
    {
        var stringBuilder = new StringBuilder();

        foreach (var entry in Data) {
            stringBuilder.Append(Regex.Escape(entry));
            stringBuilder.Append("\n");
        }

        var result = stringBuilder.ToString();
        result = result.Remove(result.Length - 1);

        return result;
    }

    public override void Store(string data)
    {
        Data.Clear();
        Data.AddRange(data.Split("\n").Select(Regex.Unescape));
        RaiseDataUpdated();
    }

    public void Set(int index, string value)
    {
        Data[index] = value;
        RaiseDataUpdated();
    }
}
