namespace Plus.HabboHotel.Items.DataFormat;

public class HighscoreDataFormat : FurniObjectData
{
    public string State = string.Empty;
    public uint ScoreType;
    public uint ClearType;

    public override FurniDataStructure StructureType => FurniDataStructure.HighScore;

    public override string Serialize()
    {
        throw new NotImplementedException();
    }

    public override void Store(string data)
    {
        throw new NotImplementedException();
    }
}
