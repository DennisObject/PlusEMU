namespace Plus.HabboHotel.Catalog;

public class CatalogDeal
{
    public int Id { get; set; }
    public string? Items { get; set; }
    public List<CatalogItem> ItemDataList { get; set; } = new();
    public string? Name { get; set; }
    public int RoomId { get; set; }
}
