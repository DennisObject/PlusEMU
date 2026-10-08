using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Xunit;

namespace Plus.Tests;

public class GamedataVersionsTests
{
    private static GamedataVersions Create(StubHandler handler, ManualClock clock, params string[] files) =>
        new(Options.Create(new GamedataVersionsConfiguration { BaseUrl = "https://assets.example.com/gamedata/", Files = files, RefreshSeconds = 30 }), clock, handler);

    [Fact]
    public async Task LooksUpEachFileWithAProbeThatBypassesCaches()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("ExternalTexts.json") ? "\"abc123\"" : "W/\"def456\"");
        var versions = await Create(handler, new ManualClock(), "ExternalTexts.json", "UITexts.jsonc").Current();

        Assert.Equal("abc123", versions["ExternalTexts.json"]);
        Assert.Equal("def456", versions["UITexts.jsonc"]);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            Assert.StartsWith("?version-probe=", request.RequestUri!.Query);
        });
    }

    [Fact]
    public async Task LeavesOutFilesWhoseLookupFails()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath.EndsWith("Missing.json") ? null : "\"abc\"");
        var versions = await Create(handler, new ManualClock(), "Missing.json", "ProductData.json").Current();

        Assert.False(versions.ContainsKey("Missing.json"));
        Assert.Equal("abc", versions["ProductData.json"]);
    }

    [Fact]
    public async Task ServesKnownVersionsUntilTheyAreRefreshed()
    {
        var tag = "\"one\"";
        var handler = new StubHandler(_ => tag);
        var clock = new ManualClock();
        var versions = Create(handler, clock, "ExternalTexts.json");

        Assert.Equal("one", (await versions.Current())["ExternalTexts.json"]);

        tag = "\"two\"";
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("one", (await versions.Current())["ExternalTexts.json"]);
        Assert.Single(handler.Requests);

        // Once stale, the old versions are served while the refresh runs; the next call sees the new ones.
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal("one", (await versions.Current())["ExternalTexts.json"]);
        await versions.PendingRefresh;
        Assert.Equal("two", (await versions.Current())["ExternalTexts.json"]);
    }

    [Fact]
    public async Task IsOffWithoutABaseUrlOrFiles()
    {
        var handler = new StubHandler(_ => "\"abc\"");

        Assert.Empty(await Create(handler, new ManualClock()).Current());
        Assert.Empty(handler.Requests);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string?> etag) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests) {
                Requests.Add(request);
            }

            await Task.Yield();

            var tag = etag(request);

            if (tag == null) {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.ETag = EntityTagHeaderValue.Parse(tag);

            return response;
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
