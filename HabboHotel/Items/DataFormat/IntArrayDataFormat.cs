namespace Plus.HabboHotel.Items.DataFormat;

public class IntArrayDataFormat : FurniObjectData
{
    private List<int> _data = new();

    public List<int> Data
    {
        get => _data;
        set
        {
            _data = value;
            RaiseDataUpdated();
        }
    }

    public override FurniDataStructure StructureType => FurniDataStructure.IntArray;

    public override string Serialize() => string.Join("\n", Data);

    public override void Store(string data)
    {
        Data.Clear();

        if (data == string.Empty)
        {
            data = "0";
        }

        Data = data.Split("\n").Select(int.Parse).ToList();
    }

    public void UpdateRange(List<int> data, int offset = 0)
    {
        if (offset < 0)
        {
            throw new IndexOutOfRangeException("Offset must be positive");
        }

        var minimumCapacity = data.Count + offset;

        if (minimumCapacity > Data.Count)
        {
            Data.AddRange(Enumerable.Repeat(0, minimumCapacity - Data.Count));
        }

        for (var i = 0; i < data.Count; i++)
        {
            Data[i + offset] = data[i];
        }

        RaiseDataUpdated();
    }

    public void Set(int index, int value)
    {
        Data[index] = value;
        RaiseDataUpdated();
    }
}
