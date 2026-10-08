namespace Plus.HabboHotel.Items.Editor;

// A furniture row as Octane's furni editor shows it (FurniItemData / FurniDetailData). PlusEMU has no lay flag,
// custom params, per-gender effects or walk clothing: those are reported as false/empty/the one effect id.
public sealed class FurniEditorItem
{
    public uint Id { get; set; }
    public int SpriteId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string PublicName { get; set; } = string.Empty;
    public string Type { get; set; } = "s";
    public int Width { get; set; }
    public int Length { get; set; }
    public double StackHeight { get; set; }
    public bool AllowStack { get; set; }
    public bool AllowWalk { get; set; }
    public bool AllowSit { get; set; }
    public bool AllowLay => false;
    public string InteractionType { get; set; } = string.Empty;
    public int InteractionModesCount { get; set; }
    public bool AllowGift { get; set; }
    public bool AllowTrade { get; set; }
    public bool AllowRecycle { get; set; }
    public bool AllowMarketplaceSell { get; set; }
    public bool AllowInventoryStack { get; set; }
    public string VendingIds { get; set; } = string.Empty;
    public string CustomParams => string.Empty;
    public int EffectId { get; set; }
    public string ClothingOnWalk => string.Empty;
    public string Multiheight { get; set; } = string.Empty;
    public string Description => string.Empty;
}

public sealed class FurniEditorCatalogRef
{
    public int Id { get; set; }
    public string CatalogName { get; set; } = string.Empty;
    public int CostCredits { get; set; }
    public int CostPoints { get; set; }
    public int PointsType { get; set; }
    public int PageId { get; set; }
    public string PageName { get; set; } = string.Empty;
}

public sealed record FurniEditorDetail(FurniEditorItem Item, int UsageCount, IReadOnlyList<FurniEditorCatalogRef> CatalogRefs, FurnidataLookup Furnidata);

public sealed record FurniEditorSearchResult(IReadOnlyList<FurniEditorItem> Items, int Total, int Page);

public sealed record FurniEditorImportResult(bool Found, string Name, string Description, string Classname);

// An editor action's answer: success, message, and the furniture id it was about.
public sealed record FurniEditorResult(bool Success, string Message, uint ItemId = 0);

// One validated furniture column change.
public sealed record FurniEditorColumnChange(string Field, string Column, object Value, object Before);

// The furnidata entry the editor shows for a furniture row, and how it was found.
public sealed record FurnidataLookup(string EntryJson, string DiagnosticJson);

// The entry before and after an edit (compact JSON) and what clients need to patch their copy.
public sealed record FurnidataEdit(string Before, string After, bool IsWallItem, int Id, string Classname, string Name, string Description)
{
    public bool Changed => Before != After;
}

public sealed class FurnidataException(string message) : Exception(message);
