using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Plus.HabboHotel.Items.Editor;
using Xunit;

namespace Plus.Tests;

// No network: a fake handler answers and records every request the importer makes.
public class FurniEditorImporterTests
{
    private const string Furnidata = """{"roomitemtypes":{"furnitype":[{"classname":"shelves_norja","name":"Beige Bookcase","description":"For books."}]}}""";

    private sealed class FakeHandler(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    private static HttpResponseMessage Ok(string body = Furnidata) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private static (FurniEditorTextImporter Importer, FakeHandler Handler) Importer(string url, Func<Uri, HttpResponseMessage> answer, long maxBytes = 1024)
    {
        var handler = new FakeHandler(answer);
        var configuration = new FurniEditorConfiguration { ImportUrl = url, ImportMaxBytes = maxBytes };
        return (new FurniEditorTextImporter(Options.Create(configuration), handler), handler);
    }

    [Fact]
    public async Task FindsOfficialTextsAndFollowsRedirectsWithinTheAllowlist()
    {
        var (importer, handler) = Importer("https://www.habbo.com/gamedata/furnidata_json/1",
            url => url.AbsolutePath.EndsWith("/1") ? Redirect("/gamedata/furnidata_json/abc123") : Ok());

        var result = await importer.Find("SHELVES_NORJA");

        Assert.Equal(new FurniEditorImportResult(true, "Beige Bookcase", "For books.", "SHELVES_NORJA"), result);
        Assert.Equal(["https://www.habbo.com/gamedata/furnidata_json/1", "https://www.habbo.com/gamedata/furnidata_json/abc123"], handler.Requests.Select(url => url.ToString()));
        Assert.False((await importer.Find("unknown"))!.Found);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://www.habbo.com/gamedata/furnidata_json/1")]
    [InlineData("https://evil.example/furnidata.json")]
    [InlineData("https://habbo.com.evil.example/furnidata.json")]
    [InlineData("https://www.habbo.com:8443/furnidata.json")]
    [InlineData("https://user@www.habbo.com/furnidata.json")]
    public async Task RefusesUrlsOutsideTheAllowlistWithoutConnecting(string url)
    {
        var (importer, handler) = Importer(url, _ => Ok());
        Assert.Null(await importer.Find("shelves_norja"));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("https://evil.example/furnidata.json")]
    [InlineData("http://www.habbo.com/gamedata/furnidata_json/2")]
    public async Task RefusesRedirectsLeavingTheAllowlist(string location)
    {
        var (importer, handler) = Importer("https://www.habbo.com/gamedata/furnidata_json/1", url => url.Host == "www.habbo.com" && url.Scheme == "https" ? Redirect(location) : Ok());
        Assert.Null(await importer.Find("shelves_norja"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task StopsEndlessRedirectsAndOversizedBodies()
    {
        var (looping, loopHandler) = Importer("https://www.habbo.com/a", _ => Redirect("https://www.habbo.com/a"));
        Assert.Null(await looping.Find("shelves_norja"));
        Assert.Equal(4, loopHandler.Requests.Count);

        var (oversized, _) = Importer("https://www.habbo.de/gamedata/furnidata_json/1", _ => Ok(), maxBytes: 20);
        Assert.Null(await oversized.Find("shelves_norja"));
    }

    [Fact]
    public void AllowlistDefaultsToTheOfficialHotelsAndCanBeReplaced()
    {
        Assert.True(FurniEditorTextImporter.IsAllowed(new Uri("https://images.habbo.com.br/x.json"), new FurniEditorConfiguration().AllowedImportHosts));
        Assert.False(FurniEditorTextImporter.IsAllowed(new Uri("https://nothabbo.com/x.json"), new FurniEditorConfiguration().AllowedImportHosts));
        var custom = new FurniEditorConfiguration { ImportAllowedHosts = ["assets.example"] };
        Assert.True(FurniEditorTextImporter.IsAllowed(new Uri("https://cdn.assets.example/x.json"), custom.AllowedImportHosts));
        Assert.False(FurniEditorTextImporter.IsAllowed(new Uri("https://www.habbo.com/x.json"), custom.AllowedImportHosts));
    }
}
