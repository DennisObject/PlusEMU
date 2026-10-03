namespace Plus.HabboHotel.Catalog.Admin;

// The draft fields every editor mutation packet ends with. PlusEMU edits the live catalog, so there is
// one version (LiveVersionId) and the revision is the newest catalog_admin_log id.
public sealed record CatalogAdminEnvelope(string CatalogType, int DraftVersionId, int ExpectedRevision, string LockToken, string Summary, string OperationId)
{
    public const int LiveVersionId = 1;
    public const int MaxOperationIdLength = 96;
    public const int MaxSummaryLength = 255;
}

public static class CatalogAdminCodes
{
    public const string Saved = "SAVED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string StaleRevision = "STALE_REVISION";
}

public sealed record CatalogAdminOutcome(
    bool Success, string Code, string Message, int Revision, string EntityType, string CatalogType, int EntityId,
    object? Entity, CatalogAdminLogEntry? History, IReadOnlyDictionary<string, string> FieldErrors);

public sealed record CatalogAdminSession(int Revision, DateTime? UpdatedAt, IReadOnlyList<CatalogAdminPage> Pages);

public sealed record CatalogAdminHistory(int Revision, int TotalCount, IReadOnlyList<CatalogAdminLogEntry> Groups);

// Thrown inside a mutation to reject it; nothing is written.
public sealed class CatalogAdminRejected(string code, string message, IReadOnlyDictionary<string, string>? fieldErrors = null) : Exception(message)
{
    public string Code { get; } = code;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
}
