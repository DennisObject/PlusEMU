namespace Plus.HabboHotel.Items.DataFormat;

public class EmptyDataFormat : FurniObjectData
{
    public override FurniDataStructure StructureType => FurniDataStructure.Empty;

    public override string Serialize() => string.Empty;

    public override void Store(string data)
    {
    }
}
