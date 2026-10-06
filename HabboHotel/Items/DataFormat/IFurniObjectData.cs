namespace Plus.HabboHotel.Items.DataFormat;

public interface IFurniObjectData
{
    FurniDataStructure StructureType { get; }
    void Store(string data);
    string Serialize();
}
