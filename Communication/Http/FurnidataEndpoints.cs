using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;
using Plus.HabboHotel.Catalog;

namespace Plus.Communication.Http;

/// <summary>
/// GET /api/gamedata/furnidata: the hotel's FurnitureData.json with purchase fields from the catalog. Clients revalidate
/// with the entity tag, so an unchanged file costs a 304.
/// </summary>
public class FurnidataEndpoints(ICatalogFurnidata furnidata)
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGet("/api/gamedata/furnidata", Furnidata);

    private IResult Furnidata(HttpContext context)
    {
        if (furnidata.Current() is not { } file) {
            return Results.NotFound();
        }

        var headers = context.Response.Headers;
        headers.CacheControl = "no-cache";
        headers.ETag = file.ETag;

        return Matches(context.Request.Headers.IfNoneMatch, file.ETag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Bytes(file.Content, "application/json; charset=utf-8");
    }

    // A proxy that compresses the response weakens the tag (W/"..."), so the weak form matches too.
    private static bool Matches(StringValues ifNoneMatch, string etag) => ifNoneMatch
        .SelectMany(value => (value ?? "").Split(','))
        .Select(tag => tag.Trim())
        .Any(tag => tag == "*" || tag == etag || tag == "W/" + etag);
}
