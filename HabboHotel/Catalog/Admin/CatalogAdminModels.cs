using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plus.HabboHotel.Catalog.Admin;

// Catalog type names of Octane's catalog studio. PlusEMU stores them as catalog_pages.catalog_mode.
public static class CatalogAdminTypes
{
    public const string Normal = "NORMAL";
    public const string Builder = "BUILDER";

    public static string FromMode(string? mode) => mode == CatalogModes.BuildersClub ? Builder : Normal;

    public static string ToMode(string type) => type == Builder ? CatalogModes.BuildersClub : CatalogModes.Normal;

    // Accepts the studio names and PlusEMU's own mode names; anything else (e.g. BOTH) is unsupported.
    public static string? Parse(string? value) => value switch
    {
        Normal => Normal,
        Builder or CatalogModes.BuildersClub => Builder,
        _ => null
    };

    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

// Field names follow Octane's CatalogStudioPageSnapshot so the JSON can be sent as the save acknowledgement entity.
public sealed record CatalogAdminPage(
    string CatalogType, int PageId, int ParentId, string CaptionSave, string Caption, string PageLayout,
    int IconColor, int IconImage, int MinRank, int OrderNum, bool Visible, bool Enabled, bool ClubOnly,
    string CatalogMode, bool VipOnly, string PageHeadline, string PageTeaser, string PageSpecial,
    string PageText1, string PageText2, string PageTextDetails, string PageTextTeaser, int RoomId, string Includes);

// Field names follow Octane's CatalogStudioOfferSnapshot. OfferId is the id the client knows the offer by.
public sealed record CatalogAdminOffer(
    string CatalogType, int OfferId, string ItemIds, int PageId, string CatalogName, int CostCredits, int CostPoints,
    int PointsType, int Amount, int LimitedStack, int OrderNumber, int OfferIdClient, int SongId, string Extradata,
    bool HaveOffer, bool ClubOnly)
{
    [JsonIgnore] public int LimitedSells { get; init; }
}

// A page move as audited: the moved page and the order of every sibling the move renumbered.
public sealed record CatalogAdminMove(CatalogAdminPage Page, IReadOnlyList<CatalogAdminOrder> Siblings);

public sealed record CatalogAdminOrder(int PageId, int OrderNum);

public sealed class CatalogPageRow
{
    public int Id { get; set; }
    public int ParentId { get; set; }
    public string Caption { get; set; } = string.Empty;
    public string PageLink { get; set; } = string.Empty;
    public int IconImage { get; set; }
    public bool Visible { get; set; }
    public bool Enabled { get; set; }
    public int MinRank { get; set; }
    public int MinVip { get; set; }
    public int OrderNum { get; set; }
    public string PageLayout { get; set; } = string.Empty;
    public string PageStrings1 { get; set; } = string.Empty;
    public string PageStrings2 { get; set; } = string.Empty;
    public string CatalogMode { get; set; } = CatalogModes.Normal;

    public CatalogPageRow Copy() => (CatalogPageRow)MemberwiseClone();
}

public sealed class CatalogOfferRow
{
    public int Id { get; set; }
    public int PageId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string CatalogName { get; set; } = string.Empty;
    public int CostCredits { get; set; }
    public int CostPixels { get; set; }
    public int CostDiamonds { get; set; }
    public int Amount { get; set; }
    public int LimitedSells { get; set; }
    public int LimitedStack { get; set; }
    public bool OfferActive { get; set; }
    public string Extradata { get; set; } = string.Empty;
    public int OfferId { get; set; }
    public int ClubLevel { get; set; }
    public int OrderNum { get; set; }
    public int HabbiconId { get; set; }

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
