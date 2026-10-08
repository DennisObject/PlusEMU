using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Plus.Communication.Http;

/// <summary>
/// GET /api/gamedata/versions: {"files": {"ExternalTexts.json": "&lt;version&gt;", ...}} for the client's entry page, which
/// then requests those files by version instead of with a per-load cache buster.
/// </summary>
public class GamedataVersionsEndpoints(IGamedataVersions versions)
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGet("/api/gamedata/versions", Versions);

    // Written directly: a (HttpContext) => Task<IResult> handler binds as a RequestDelegate and its result is dropped.
    private async Task Versions(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsJsonAsync(new { files = await versions.Current() });
    }
}
