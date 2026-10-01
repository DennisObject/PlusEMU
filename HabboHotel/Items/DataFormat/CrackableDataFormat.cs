using System.Text.RegularExpressions;

namespace Plus.HabboHotel.Items.DataFormat;

public class CrackableDataFormat : FurniObjectData
{
    public string State = string.Empty;
    public uint Hits;
    public uint Target;

    private readonly Lock _lock = new();

    public override FurniDataStructure StructureType => FurniDataStructure.Crackable;
    public override string Serialize() => $"{Regex.Escape(State)}\n{Hits}\n{Target}";

    public override void Store(string data)
    {
        var d = data.Split("\n");
        if (d.Length != 3) return;
        State = Regex.Unescape(d[0]);
        uint.TryParse(d[1], out Hits);
        uint.TryParse(d[2], out Target);
    }

    public bool TryCrack()
    {
        lock (_lock)
        {
            if (Hits < Target)
                Hits++;

            RaiseDataUpdated();
            return Hits == Target;
        }
    }
}