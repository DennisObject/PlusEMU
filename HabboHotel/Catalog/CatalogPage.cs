using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog;

public class CatalogPage
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public bool Enabled { get; set; }

    public string? Caption { get; set; }

    public string? Link { get; set; }

    public int Icon { get; set; }

    public int MinimumRank { get; set; }

    public int MinimumVip { get; set; }

    public bool Visible { get; set; }

    public string Layout { get; set; }

    public string CatalogMode { get; set; } = CatalogModes.Normal;

    public string? PageStrings1 { get; set; }

    public string? PageStrings2 { get; set; }

    public List<string> PageStringsList1 { get; set; } = new();

    public List<string> PageStringsList2 { get; set; } = new();

    public Dictionary<int, CatalogItem> Items { get; set; } = new();

    public Dictionary<int, CatalogItem> ItemOffers { get; set; } = new();

    // Rank and VIP gates. Hidden pages (Visible = false) stay reachable by link, as on the official hotel.
    public bool IsAvailableTo(Habbo habbo) => MinimumRank <= habbo.Rank && (MinimumVip <= habbo.VipRank || habbo.Rank != 1);

    public bool CanOpen(Habbo habbo) => Enabled && IsAvailableTo(habbo);

    public CatalogItem? GetItem(int pId)
    {
        if (Items.ContainsKey(pId))
            return Items[pId];
        return null;
    }
}