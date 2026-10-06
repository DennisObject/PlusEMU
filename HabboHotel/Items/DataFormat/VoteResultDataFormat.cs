using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Items.DataFormat;

public class VoteResultDataFormat : FurniObjectData
{
    public string State = string.Empty;
    public int Result;

    public override FurniDataStructure StructureType => FurniDataStructure.VoteResult;

    public override string Serialize()
    {
        return $"{Regex.Escape(State)}\n{Result}";
    }

    public override void Store(string data)
    {
        if (string.IsNullOrWhiteSpace(data)) {
            return;
        }

        var d = data.Split("\n");

        if (d.Length != 2) {
            return;
        }

        State = Regex.Unescape(d[0]);
        int.TryParse(d[1], out Result);
    }
}
