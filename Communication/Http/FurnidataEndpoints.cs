using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Plus.HabboHotel.Catalog;

namespace Plus.Communication.Http;

/// <summary>
/// GET /api/gamedata/furnidata: the hotel's FurnitureData.json with purchase fields from the catalog. Clients revalidate
/// with the entity tag, so an unchanged file costs a 304. Requested with ?v= set to the current version it never changes,
/// so it may be cached for good, at the edge too.
/// GET /api/gamedata/furnidata/version: that version (the SHA-1 of the content), for the client's entry page.
/// </summary>
public class FurnidataEndpoints(ICatalogFurnidata furnidata)
{
    private const string Immutable = "public, max-age=31536000, immutable";

    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/gamedata/furnidata", Furnidata);
        routes.MapGet("/api/gamedata/furnidata/version", Version);
    }

    private IResult Furnidata(HttpContext context)
    {
        if (furnidata.Current() is not { } file) {
            return Results.NotFound();
        }

        // Another version than the current one gets the current content, but only revalidatable.
        var versioned = context.Request.Query["v"] == VersionOf(file);
        var headers = context.Response.Headers;
        headers.CacheControl = versioned ? Immutable : "no-cache";
        headers.ETag = file.ETag;

        return Matches(context.Request.Headers.IfNoneMatch, file.ETag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Bytes(file.Content, "application/json; charset=utf-8");
    }

    private IResult Version(HttpContext context)
    {
        if (furnidata.Current() is not { } file) {
            return Results.NotFound();
        }

        context.Response.Headers.CacheControl = "no-store";

        return Results.Json(new { version = VersionOf(file) });
    }

    // The entity tag without its quotes.
    private static string VersionOf(CatalogFurnidataFile file) => file.ETag.Trim('"');

    // A proxy that compresses the response weakens the tag (W/"..."), so the weak form matches too.
    private static bool Matches(StringValues ifNoneMatch, string etag) => ifNoneMatch
        .SelectMany(value => (value ?? "").Split(','))
        .Select(tag => tag.Trim())
        .Any(tag => tag == "*" || tag == etag || tag == "W/" + etag);
}
