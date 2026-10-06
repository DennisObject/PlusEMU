namespace Plus.HabboHotel.Items.DataFormat;

public class ListListDataFormat : FurniObjectData
{
    private List<List<string>> _data = new(0);
    public List<List<string>> Data
    {
        get => _data;
        set
        {
            _data = value;
            RaiseDataUpdated();
        }
    }

    public override FurniDataStructure StructureType => FurniDataStructure.ListList;

    public override string Serialize()
    {
        return string.Join(";", Data.Select(d => string.Join(",", d)));
    }

    public override void Store(string data)
    {
        Data = data.Split(";").Select(d => d.Split(",").ToList()).ToList();
    }
}
