using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plus.HabboHotel.Camera;
using Xunit;

namespace Plus.Tests;

public class CameraEffectCatalogueTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void EffectCatalogueComesFromPrivateEffectsRoute()
    {
        Assert.Null(CameraEffectCatalogue.Parse("""[{"name":"frame_gold.png","minLevel":0,"type":"frame"}]"""));
        Assert.Null(CameraEffectCatalogue.Parse("""[{"name":"dark_sepia","minLevel":0,"type":"composite","file":"a.png"}]"""));
        var parsed = CameraEffectCatalogue.Parse("""[{"name":"Yellow","minLevel":6,"type":"colormatrix"}]""");
        Assert.Equal(new CameraEffectDefinition("Yellow", 6, "colormatrix"), Assert.Single(parsed!));

        var handler = new ScriptedHandler("""[{"name":"dark_sepia","minLevel":0,"type":"composite"}]""");
        var time = new ManualTime(Now);
        var catalogue = new CameraEffectCatalogue(Options.Create(new CameraConfiguration
        {
            RendererUrl = "http://127.0.0.1:5078/render",
            Bearer = new string('x', 32)
        }), time, NullLogger<CameraEffectCatalogue>.Instance, handler);

        Assert.Equal("dark_sepia", catalogue.Current().Single().Name);
        catalogue.Current();
        Assert.Equal(1, handler.Calls);
        time.Now = Now.AddSeconds(31);
        handler.Status = HttpStatusCode.Unauthorized;
        Assert.Empty(catalogue.Current());

        var publicHost = new ScriptedHandler("[]");
        var blocked = new CameraEffectCatalogue(Options.Create(new CameraConfiguration
        {
            RendererUrl = "http://8.8.8.8/render",
            Bearer = new string('x', 32)
        }), time, NullLogger<CameraEffectCatalogue>.Instance, publicHost);
        Assert.Empty(blocked.Current());
        Assert.Equal(0, publicHost.Calls);
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ScriptedHandler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("/effects", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(new string('x', 32), request.Headers.Authorization.Parameter);
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
