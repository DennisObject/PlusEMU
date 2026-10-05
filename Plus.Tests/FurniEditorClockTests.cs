using System.Net;
using Microsoft.Extensions.Options;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Catalog.Admin;
using Plus.HabboHotel.Items.Editor;
using Xunit;

namespace Plus.Tests;

public sealed class FurniEditorClockTests
{
    [Fact]
    public void EditingCooldownReadsOneInstantAndAllowsTheExactSecond()
    {
        var clock = new Clock(new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.FromHours(9)));
        var service = Service(clock);
        var actor = EditorTestSupport.Staff();
        Assert.NotEqual("Too many requests", service.UpdateFurnidata(actor, 7, "{").Message);
        clock.Now = clock.Now.AddSeconds(1).AddTicks(-1);
        Assert.Equal("Too many requests", service.UpdateFurnidata(actor, 7, "{").Message);
        clock.Now = clock.Now.AddTicks(1);
        Assert.NotEqual("Too many requests", service.UpdateFurnidata(actor, 7, "{").Message);
        Assert.Equal(3, clock.Reads);
    }

    [Fact]
    public void ConcurrentRequestsForOneEditorTakeOnlyOneCooldownTurn()
    {
        var clock = new Clock(DateTimeOffset.MaxValue);
        var service = Service(clock);
        var actor = EditorTestSupport.Staff();
        var results = new string[16];
        Parallel.For(0, results.Length, index => results[index] = service.UpdateFurnidata(actor, 7, "{").Message);
        Assert.Single(results, message => message != "Too many requests");
        Assert.Equal(16, clock.Reads);
        clock.Now = clock.Now.AddTicks(-1);
        Assert.Equal("Too many requests", service.UpdateFurnidata(actor, 7, "{").Message);
    }

    [Fact]
    public async Task ImportCacheExpiresAtExactlyTenMinutesOnTheInjectedClock()
    {
        var clock = new Clock(new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.FromHours(5.5)));
        var handler = new Handler();
        using var importer = new FurniEditorTextImporter(Options.Create(new FurniEditorConfiguration
            { ImportUrl = "https://www.habbo.com/furnidata.json" }), clock, handler);
        Assert.Equal("Name 1", (await importer.Find("chair"))!.Name);
        Assert.Equal(1, handler.Requests);
        clock.Now = clock.Now.AddMinutes(10).AddTicks(-1);
        Assert.Equal("Name 1", (await importer.Find("chair"))!.Name);
        Assert.Equal(1, handler.Requests);
        clock.Now = clock.Now.AddTicks(1);
        Assert.Equal("Name 2", (await importer.Find("chair"))!.Name);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(4, clock.Reads);
    }

    private static FurniEditorService Service(TimeProvider clock) => new(
        EditorTestSupport.UntouchableDatabase(),
        CatalogSnapshotTestSupport.Proxy<IFurnidataStore>((method, _) => throw new NotSupportedException(method)),
        CatalogSnapshotTestSupport.Proxy<IFurniEditorTextImporter>((method, _) => throw new NotSupportedException(method)),
        CatalogSnapshotTestSupport.Proxy<ICatalogCacheRefresher>((method, _) => throw new NotSupportedException(method)),
        CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, _) => throw new NotSupportedException(method)),
        TestLogging.For<FurniEditorService>(), clock);

    private sealed class Handler : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"roomitemtypes\":{\"furnitype\":[{\"classname\":\"chair\",\"name\":\"Name " + Requests + "\",\"description\":\"Text\"}]}}")
            });
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public int Reads;
        public override DateTimeOffset GetUtcNow() { Interlocked.Increment(ref Reads); return Now; }
    }
}
