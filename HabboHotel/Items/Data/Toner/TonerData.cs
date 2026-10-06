using Dapper;

namespace Plus.HabboHotel.Items.Data.Toner;

public class TonerData
{
    public int Enabled;
    public int Hue;
    public uint ItemId;
    public int Lightness;
    public int Saturation;

    public TonerData(uint item)
    {
        ItemId = item;
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        var row = connection.QuerySingleOrDefault<TonerRow>(
            "SELECT enabled,data1 AS Hue,data2 AS Saturation,data3 AS Lightness FROM room_items_toner WHERE id=@itemId LIMIT 1",
            new { itemId = ItemId });
        if (row == null)
        {
            connection.Execute("INSERT INTO room_items_toner (id,enabled,data1,data2,data3) VALUES (@itemId,FALSE,0,0,0)", new { itemId = ItemId });
            row = new(false, 0, 0, 0);
        }
        Enabled = row.Enabled ? 1 : 0;
        Hue = row.Hue;
        Saturation = row.Saturation;
        Lightness = row.Lightness;
    }

    private sealed record TonerRow(bool Enabled, int Hue, int Saturation, int Lightness);
}
