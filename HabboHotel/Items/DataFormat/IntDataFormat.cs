namespace Plus.HabboHotel.Items.DataFormat;

public class IntDataFormat : FurniObjectData
{
    private int _data;

    public int Data
    {
        get => _data;
        set
        {
            _data = value;
            RaiseDataUpdated();
        }
    }

    public override FurniDataStructure StructureType => FurniDataStructure.Int;

    public override string Serialize() => _data.ToString();

    public override void Store(string data) => int.TryParse(data, out _data);
}
