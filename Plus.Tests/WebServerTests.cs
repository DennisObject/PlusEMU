using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.HabboHotel.Badges.Rarity;
using Plus.HabboHotel.Catalog;
using Xunit;

namespace Plus.Tests;

public sealed class WebServerTests : IAsyncLifetime
{
    private BadgeLeaderboardSnapshot _badges = BadgeLeaderboardSnapshot.Empty;
    private CatalogFurnidataFile? _furnidata;
    private HttpClient _http = new();
    private WebServer? _server;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _http.Dispose();

        if (_server != null) {
            await _server.Stop();
        }
    }

    private async Task Start(Action<WebServerConfiguration>? configure = null)
    {
        var configuration = new WebServerConfiguration { Port = 0 };
        configure?.Invoke(configuration);
        _server = new WebServer(Options.Create(configuration), new FixedBadgeRarity(_badges), new FixedFurnidata(_furnidata));
        await _server.Start();
        _http.Dispose();
        _http = new HttpClient { BaseAddress = new Uri(_server.Urls.Single()) };
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task LeaderboardResponsesCarrySafeHeaders()
    {
        await Start();

        var response = await _http.GetAsync("/api/badges/leaderboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task TheHostLeavesStopSignalsToTheEmulatorAndStopsWhenAsked()
    {
        await Start();

        // ASP.NET's default ConsoleLifetime swallows SIGTERM and waits for a host Run() that the
        // emulator never calls, which kept the process alive.
        var lifetime = _server!.Services!.GetRequiredService<Microsoft.Extensions.Hosting.IHostLifetime>();
        Assert.DoesNotContain("Console", lifetime.GetType().Name);

        var url = _server.Urls.Single();
        await _server.Stop();
        _server = null;
        await Assert.ThrowsAsync<HttpRequestException>(() => new HttpClient().GetAsync(url + "/api/badges/leaderboard"));
    }

    [Fact]
    public async Task UnknownRoutesAre404NotAnEmptySuccess()
    {
        await Start();

        var response = await _http.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not found.", (await Json(response)).GetProperty("error").GetString());
        Assert.Equal(WebErrors.NotFound, (await Json(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task BadgeLeaderboardScalesTiersToThePopulation()
    {
        // At 1,000 active players rare reaches 150 owners and legendary 5, where fixed bands stopped at 50 and 8.
        var ownership = Enumerable.Range(1, 150).Select(id => new BadgeOwnership(id, "RARE"))
            .Concat(Enumerable.Range(201, 5).Select(id => new BadgeOwnership(id, "LEGEND")))
            .Append(new BadgeOwnership(7, "SOLO"));
        _badges = BadgeLeaderboardSnapshot.Build(ownership, [new(7, 30)], BadgeRarityScale.For(1000, null));
        await Start();

        var body = await Json(await _http.GetAsync("/api/badges/leaderboard"));

        Assert.Equal(0, body.GetProperty("viewerUserId").GetInt32());
        Assert.Equal(1000, body.GetProperty("population").GetInt32());
        Assert.Equal(150, body.GetProperty("thresholds").GetProperty("rare").GetInt32());
        Assert.Equal(5, body.GetProperty("thresholds").GetProperty("legendary").GetInt32());
        var stats = body.GetProperty("badgeStats").EnumerateArray().ToDictionary(stat => stat.GetProperty("badgeCode").GetString()!,
            stat => (stat.GetProperty("ownerCount").GetInt32(), stat.GetProperty("rarity").GetString()));
        Assert.Equal((150, "rare"), stats["RARE"]);
        Assert.Equal((5, "legendary"), stats["LEGEND"]);
        Assert.Equal((1, "unique"), stats["SOLO"]);
        var boards = body.GetProperty("leaderboards");
        Assert.Equal(["rare", "epic", "mythical", "legendary", "unique"], boards.GetProperty("rarity").EnumerateObject().Select(board => board.Name));
        Assert.False(boards.GetProperty("rarity").GetProperty("unique").TryGetProperty("viewerEntry", out _));
        Assert.Equal(BadgeLeaderboardSnapshot.EntryLimit, boards.GetProperty("totalBadges").GetProperty("totalPlayers").GetInt32());
        Assert.False(boards.GetProperty("achievementLevel").TryGetProperty("viewerEntry", out _));
    }

    [Fact]
    public async Task BadgeLeaderboardIsAnonymous()
    {
        _badges = BadgeLeaderboardSnapshot.Build([new(7, "SOLO")], [], BadgeRarityScale.Empty);
        await Start();

        var body = await Json(await _http.GetAsync("/api/badges/leaderboard"));

        Assert.Equal(0, body.GetProperty("viewerUserId").GetInt32());
        Assert.False(body.GetProperty("leaderboards").GetProperty("totalBadges").TryGetProperty("viewerEntry", out _));
    }

    [Fact]
    public async Task FurnidataRevalidatesWithItsEntityTag()
    {
        await Start();
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/gamedata/furnidata")).StatusCode);
        await _server!.Stop();

        _furnidata = new("{\"roomitemtypes\":{}}"u8.ToArray(), "\"abc\"");
        await Start();

        var response = await _http.GetAsync("/api/gamedata/furnidata?t=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"roomitemtypes\":{}}", await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("\"abc\"", response.Headers.ETag!.Tag);
        Assert.True(response.Headers.CacheControl!.NoCache);
        Assert.False(response.Headers.CacheControl.NoStore);

        foreach (var tag in new[] { "\"abc\"", "W/\"abc\"", "\"old\", \"abc\"" }) {
            var revalidate = new HttpRequestMessage(HttpMethod.Get, "/api/gamedata/furnidata");
            revalidate.Headers.TryAddWithoutValidation("If-None-Match", tag);
            var notModified = await _http.SendAsync(revalidate);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
            Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());
        }

        var stale = new HttpRequestMessage(HttpMethod.Get, "/api/gamedata/furnidata");
        stale.Headers.TryAddWithoutValidation("If-None-Match", "\"old\"");
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(stale)).StatusCode);
    }

    [Fact]
    public async Task FurnidataAtItsVersionIsImmutable()
    {
        await Start();
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/api/gamedata/furnidata/version")).StatusCode);
        await _server!.Stop();

        _furnidata = new("{\"roomitemtypes\":{}}"u8.ToArray(), "\"abc\"");
        await Start();

        var version = await _http.GetAsync("/api/gamedata/furnidata/version");
        Assert.Equal(HttpStatusCode.OK, version.StatusCode);
        Assert.True(version.Headers.CacheControl!.NoStore);
        Assert.Equal("abc", (await Json(version)).GetProperty("version").GetString());

        var current = await _http.GetAsync("/api/gamedata/furnidata?v=abc");
        Assert.Equal("{\"roomitemtypes\":{}}", await current.Content.ReadAsStringAsync());
        Assert.True(current.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromDays(365), current.Headers.CacheControl.MaxAge);
        Assert.Contains("immutable", current.Headers.CacheControl.Extensions.Select(extension => extension.Name));

        // An old version still gets the current content, which must not be cached for good.
        var old = await _http.GetAsync("/api/gamedata/furnidata?v=old");
        Assert.Equal("{\"roomitemtypes\":{}}", await old.Content.ReadAsStringAsync());
        Assert.True(old.Headers.CacheControl!.NoCache);
        Assert.Null(old.Headers.CacheControl.MaxAge);
    }

    private sealed class FixedFurnidata(CatalogFurnidataFile? file) : ICatalogFurnidata
    {
        public CatalogFurnidataFile? Current() => file;
    }

    private sealed class FixedBadgeRarity(BadgeLeaderboardSnapshot snapshot) : IBadgeRarityManager
    {
        public BadgeLeaderboardSnapshot Snapshot => snapshot;

        public Task Refresh() => Task.CompletedTask;

        public Task<LeaderboardProfile?> GetProfile(int userId) => Task.FromResult<LeaderboardProfile?>(new("Viewer" + userId, "hd-180-1"));
    }
}
