namespace Plus.HabboHotel.Items.DataFormat;

public abstract class FurniObjectData : IFurniObjectData
{
    public static FurniObjectData Empty = new EmptyDataFormat();
    public abstract FurniDataStructure StructureType
    {
        get;
    }
    public abstract string Serialize();
    public abstract void Store(string data);
    public event EventHandler<EventArgs>? DataUpdated;

    /// TODO: Default interactions with no multistate can have Empty.
    public static FurniObjectData FromStructure(FurniDataStructure structure) =>
        structure switch
        {
            FurniDataStructure.Empty => Empty,
            FurniDataStructure.Legacy => new LegacyDataFormat(),
            FurniDataStructure.Map => new MapDataFormat(),
            FurniDataStructure.StringArray => new StringArrayDataFormat(),
            FurniDataStructure.IntArray => new IntArrayDataFormat(),
            FurniDataStructure.VoteResult => new VoteResultDataFormat(),
            FurniDataStructure.HighScore => new HighscoreDataFormat(),
            FurniDataStructure.Crackable => new CrackableDataFormat(),
            FurniDataStructure.ListList => new ListListDataFormat(),
            FurniDataStructure.Int => new IntDataFormat(),
            _ => throw new ArgumentException("Could not identify FurniDataStructure from ", structure.ToString()),
        };

    protected virtual void RaiseDataUpdated()
    {
        DataUpdated?.Invoke(this, EventArgs.Empty);
    }
}
