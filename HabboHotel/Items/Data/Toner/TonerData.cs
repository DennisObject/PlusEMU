namespace Plus.HabboHotel.Items.Data.Toner;

public class TonerData
{
    public int Enabled;
    public int Hue;
    public uint ItemId;
    public int Lightness;
    public int Saturation;

    // Pure model: the typed record is loaded by the metadata store.
    public TonerData(uint item, TonerRecord record)
    {
        ItemId = item;
        Enabled = record.Enabled ? 1 : 0;
        Hue = record.Hue;
        Saturation = record.Saturation;
        Lightness = record.Lightness;
    }
}
