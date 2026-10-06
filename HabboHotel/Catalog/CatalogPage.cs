using Plus.HabboHotel.Users;
using Plus.HabboHotel.Subscriptions;

namespace Plus.HabboHotel.Catalog;

public class CatalogPage
{
    public int Id { get; set; }

    public int ParentId { get; set; }

    public bool Enabled { get; set; }

    public string? Caption { get; set; }

    public string? Link { get; set; }

    public int Icon { get; set; }

    public string? RequiredPermission { get; set; }

    public int RequiredClubLevel { get; set; }

    public bool Visible { get; set; }

    public string Layout { get; set; } = string.Empty;


    public string? PageStrings1 { get; set; }

    public string? PageStrings2 { get; set; }

    public List<string> PageStringsList1 { get; set; } = new();

    public List<string> PageStringsList2 { get; set; } = new();

    public Dictionary<int, CatalogItem> Items { get; set; } = new();

    // Offers by WireOfferId, in display order. Pages, purchases, gifts and preselection all resolve here.
    public Dictionary<int, CatalogItem> Offers { get; set; } = new();

    // Permission gates. Hidden pages (Visible = false) stay reachable by link, as on the official hotel.
    public bool IsAvailableTo(Habbo habbo)
    {
        var access = habbo.Access.Capture(out var now);

        return (string.IsNullOrEmpty(RequiredPermission) || access.Keys.Contains(RequiredPermission)) &&
            ClubAccess.LevelFor(access, now) >= RequiredClubLevel;
    }

    public bool CanOpen(Habbo habbo) => Enabled && IsAvailableTo(habbo);

    public CatalogItem? GetItem(int pId)
    {
        if (Items.ContainsKey(pId)) {
            return Items[pId];
        }

        return null;
    }
}
