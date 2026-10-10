using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plus.HabboHotel.Catalog.Admin;

public static class CatalogAdminTypes
{
    public const string Normal = "NORMAL";

    public static string? Parse(string? value) => value == Normal ? Normal : null;

    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

// Field names follow Volt's CatalogStudioPageSnapshot so the JSON can be sent as the save acknowledgement entity.
public sealed record CatalogAdminPage(
    string CatalogType, int PageId, int ParentId, string CaptionSave, string Caption, string PageLayout,
    int IconColor, int IconImage, string RequiredPermission, int OrderNum, bool Visible, bool Enabled, bool ClubOnly,
    string CatalogMode, string PageHeadline, string PageTeaser, string PageSpecial,
    string PageText1, string PageText2, string PageTextDetails, string PageTextTeaser, int RoomId, string Includes);

// Field names follow Volt's CatalogStudioOfferSnapshot. OfferId is the id the client knows the offer by.
public sealed record CatalogAdminOffer(
    string CatalogType, int OfferId, string ItemIds, int PageId, string CatalogName, int CostCredits, int CostPoints,
    int PointsType, int Amount, int LimitedStack, int OrderNumber, int OfferIdClient, int SongId, string Extradata,
    bool HaveOffer, bool ClubOnly)
{
    [JsonIgnore] public int LimitedSells { get; init; }
}

// A page move as audited: the moved page and the place (parent, catalog, order) of every sibling the move renumbered.
public sealed record CatalogAdminMove(CatalogAdminPage Page, IReadOnlyList<CatalogAdminOrder> Siblings);

public sealed record CatalogAdminOrder(int PageId, int ParentId, string CatalogMode, int OrderNum);

public sealed class CatalogPageRow
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Caption { get; set; } = string.Empty;
    public string PageLink { get; set; } = string.Empty;
    public int IconImage { get; set; }
    public bool Visible { get; set; }
    public bool Enabled { get; set; }
    public string? RequiredPermission { get; set; }
    public int RequiredClubLevel { get; set; }
    public int OrderNum { get; set; }
    public string PageLayout { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public List<string> Texts { get; set; } = new();

    public CatalogPageRow Copy()
    {
        var copy = (CatalogPageRow)MemberwiseClone();
        copy.Images = new(Images);
        copy.Texts = new(Texts);

        return copy;
    }
}

// An offer as placed on one page. ItemId names the furniture of a single-furniture offer and is empty otherwise;
// only such offers can change what they sell in the editor.
public sealed class CatalogOfferRow
{
    public int Id { get; set; }
    public int PageId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string CatalogName { get; set; } = string.Empty;
    public int CostCredits { get; set; }
    public int CostPoints { get; set; }
    public int PointsType { get; set; }
    public int Amount { get; set; }
    public int LimitedSells { get; set; }
    public int LimitedStack { get; set; }
    public bool BulkPurchase { get; set; }
    public bool Enabled { get; set; }
    public string Extradata { get; set; } = string.Empty;
    public int ClubLevel { get; set; }
    public int OrderNum { get; set; }
    public int HabbiconId { get; set; }

    // Position of the product the editor edits; -1 when there is none to edit.
    public int ProductPosition { get; set; } = -1;

    public bool HasEditableItem => HabbiconId == 0 && ProductPosition >= 0;

    // The official Habbo offer id, or -1 for an offer numbered by this hotel.
    public int OfficialOfferId => Id is > 0 and < CatalogOfferIndex.CustomOfferIdBase ? Id : -1;

    public CatalogOfferRow Copy() => (CatalogOfferRow)MemberwiseClone();
}

// One audit row; its id is the catalog revision after the change.
public sealed class CatalogAdminLogEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string CatalogType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
