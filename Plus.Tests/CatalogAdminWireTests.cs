using System.IO.Compression;
using System.Text.Json;
using Plus.Communication.Packets.Incoming.Catalog.Admin;
using Plus.Communication.Packets.Outgoing.Catalog.Admin;
using Plus.HabboHotel.Catalog.Admin;
using Xunit;

namespace Plus.Tests;

// Field orders follow the RBAC catalog editor wire contract.
public class CatalogAdminWireTests
{
    private static readonly CatalogAdminPage Page = new("NORMAL", 42, 7, "guild_shop", "Guild shop", "guild_furni", 1, 145, "catalog.pages.guild", 9, true, false,
        false, "NORMAL", "headline", "teaser", "", "text one", "", "details", "teaser text", 0, "");

    [Fact]
    public void SavePageReadsEveryFieldAndTheDraftEnvelope()
    {
        var packet = EditorTestSupport.Incoming(42, "Guild shop", "guild_shop", "guild_furni", 145, "catalog.pages.guild", true, false, 9, 7,
            "headline", "teaser", "details", "NORMAL", "NORMAL", "text one", 3, true, "special", "text two", "teaser text", 123, "1;2;3",
            12, 7, "token-123", "Updated page: Guild shop", "save-page-1");

        var (page, envelope) = CatalogAdminPacketReader.SavePage(packet);

        Assert.Equal(new CatalogAdminPage("NORMAL", 42, 7, "guild_shop", "Guild shop", "guild_furni", 3, 145, "catalog.pages.guild", 9, true, false, true, "NORMAL",
            "headline", "teaser", "special", "text one", "text two", "details", "teaser text", 123, "1;2;3"), page);
        Assert.Equal(new CatalogAdminEnvelope("NORMAL", 12, 7, "token-123", "Updated page: Guild shop", "save-page-1"), envelope);
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public void CreatePageReadsTheCreateOrder()
    {
        var packet = EditorTestSupport.Incoming("Guild shop", "guild_shop", "guild_furni", 145, "catalog.pages.guild", true, false, 9, 7,
            "NORMAL", "NORMAL", 3, false, "headline", "teaser", "special", "text one", "text two", "details", "teaser text", 0, "",
            1, 4, "", "Created page: Guild shop", "create-page-1");

        var (page, envelope) = CatalogAdminPacketReader.CreatePage(packet);

        Assert.Equal(new CatalogAdminPage("NORMAL", 0, 7, "guild_shop", "Guild shop", "guild_furni", 3, 145, "catalog.pages.guild", 9, true, false, false, "NORMAL",
            "headline", "teaser", "special", "text one", "text two", "details", "teaser text", 0, ""), page);
        Assert.Equal(("create-page-1", 4), (envelope.OperationId, envelope.ExpectedRevision));
        Assert.False(packet.HasDataRemaining());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OffersReadSaveAndCreateOrders(bool save)
    {
        object[] fields = [42, "100:2", "sound_offer", 5, 0, 0, 1, 1, "", true, 77, 0, -1, 321, "NORMAL", 12, 7, "offer-token", "Updated offer", "save-offer-1"];
        var packet = EditorTestSupport.Incoming(save ? [99, .. fields] : fields);

        var (offer, envelope) = CatalogAdminPacketReader.Offer(packet, save);

        Assert.Equal(new CatalogAdminOffer("NORMAL", save ? 99 : 0, "100:2", 42, "sound_offer", 5, 0, 0, 1, 0, -1, 77, 321, "", true, true), offer);
        Assert.Equal("save-offer-1", envelope.OperationId);
        Assert.False(packet.HasDataRemaining());
    }

    [Fact]
    public void ReorderReadsTheBatchAndRefusesOversizedCounts()
    {
        var reorder = CatalogAdminPacketReader.ReorderOffers(EditorTestSupport.Incoming(2, 10, 0, 11, 1, "NORMAL", 0, 0, "", "", ""));

        Assert.NotNull(reorder);
        Assert.Equal([(10, 0), (11, 1)], reorder.Value.Orders);
        Assert.Null(CatalogAdminPacketReader.ReorderOffers(EditorTestSupport.Incoming(501)));
        Assert.Null(CatalogAdminPacketReader.ReorderOffers(EditorTestSupport.Incoming(-1)));
    }

    [Fact]
    public void SmartSaveSuccessCarriesTheAcknowledgementTheEditorExpects()
    {
        var history = new CatalogAdminLogEntry { Id = 8, UserId = 7001, Username = "editor", EntityType = "PAGE", CatalogType = "NORMAL", EntityId = 42,
            Operation = "UPDATE", Summary = "Updated page: Guild shop", CreatedAt = new DateTime(2026, 10, 4, 12, 0, 0) };
        var outcome = new CatalogAdminOutcome(true, "SAVED", "Page saved", 8, "PAGE", "NORMAL", 42, Page, history, new Dictionary<string, string>());
        var packet = new HabbiconTestSupport.RecordingPacket();

        new CatalogAdminResultComposer(true, "Page saved", new("save-page-1", "savePage", outcome, "editor")).Compose(packet);

        Assert.Equal(new object[] { true, "Page saved", 1, "save-page-1", "savePage", "SAVED", 1, 8, "PAGE", "NORMAL", 42 }, packet.Writes.Take(11));
        using var entity = JsonDocument.Parse((string)packet.Writes[11]);
        Assert.Equal(42, entity.RootElement.GetProperty("pageId").GetInt32());
        Assert.Equal("NORMAL", entity.RootElement.GetProperty("catalogType").GetString());
        Assert.Equal("guild_furni", entity.RootElement.GetProperty("pageLayout").GetString());
        using var group = JsonDocument.Parse((string)packet.Writes[12]);
        Assert.Equal(8, group.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal("editor", group.RootElement.GetProperty("actorName").GetString());
        Assert.Equal("2026-10-04T12:00:00", group.RootElement.GetProperty("createdAt").GetString());
        var entry = group.RootElement.GetProperty("entries")[0];
        Assert.Equal(("PAGE", 42, "UPDATE"), (entry.GetProperty("entityType").GetString(), entry.GetProperty("entityId").GetInt32(), entry.GetProperty("operation").GetString()));
        Assert.Equal(new object[] { "{}", 0 }, packet.Writes.Skip(13));
    }

    [Fact]
    public void SmartSaveFailureCarriesCodeAndFieldErrorsWithoutEntity()
    {
        var outcome = new CatalogAdminOutcome(false, "VALIDATION_FAILED", "caption: Required.", 3, "OFFER", "NORMAL", 0, null, null,
            new Dictionary<string, string> { ["catalogName"] = "Required." });
        var packet = new HabbiconTestSupport.RecordingPacket();

        new CatalogAdminResultComposer(false, outcome.Message, new("create-offer-1", "createOffer", outcome, "editor")).Compose(packet);

        Assert.Equal(new object[] { false, "caption: Required.", 1, "create-offer-1", "createOffer", "VALIDATION_FAILED", 1, 3, "OFFER", "NORMAL", 0,
            "", "null", "{\"catalogName\":\"Required.\"}", 0 }, packet.Writes);
    }

    [Fact]
    public void PlainResultsHaveNoAcknowledgement()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new CatalogAdminResultComposer(true, "Page deleted").Compose(packet);
        Assert.Equal(new object[] { true, "Page deleted" }, packet.Writes);
    }

    [Fact]
    public void PageDetailsFollowTheParserOrder()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new CatalogAdminPageDetailsComposer(Page).Compose(packet);
        Assert.Equal(new object[] { 42, "Guild shop", "guild_shop", 7, "NORMAL", "guild_furni", 1, 145, "catalog.pages.guild", 9, true, false, false,
            "headline", "teaser", "", "text one", "", "details", "teaser text", 0, "" }, packet.Writes);
    }

    [Fact]
    public void OfferDetailsFollowTheParserOrder()
    {
        var offer = new CatalogAdminOffer("NORMAL", 99, "100", 42, "sound_offer", 5, 2, 5, 1, 10, 3, 77, 0, "x", true, true) { LimitedSells = 4 };
        var packet = new HabbiconTestSupport.RecordingPacket();
        new CatalogAdminOfferDetailsComposer(offer).Compose(packet);
        Assert.Equal(new object[] { 99, 42, "100", "sound_offer", 5, 2, 5, 1, true, "x", true, 77, 10, 4, 3, 0, "NORMAL" }, packet.Writes);
    }

    [Fact]
    public void SessionSendsLiveVersionAndGzippedPageChunks()
    {
        var pages = Enumerable.Range(1, 3000).Select(id => Page with { PageId = id, Caption = $"Page {id} {Guid.NewGuid()}" }).ToList();
        var packet = new HabbiconTestSupport.RecordingPacket();

        new CatalogStudioSessionComposer(new(5, new DateTime(2026, 10, 4, 12, 0, 0), pages)).Compose(packet);

        Assert.Equal(new object[] { 1, 1, 5, "2026-10-04T12:00:00", "2026-10-04T12:00:00", 0, 0, true, 0, 0, "GZIP_BASE64_JSON" }, packet.Writes.Take(11));
        int chunkCount = (int)packet.Writes[11];
        var chunks = packet.Writes.Skip(12).Cast<string>().ToList();
        Assert.True(chunkCount > 1);
        Assert.Equal(chunkCount, chunks.Count);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= CatalogStudioSessionComposer.ChunkLength));
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(string.Concat(chunks))), CompressionMode.Decompress);
        using var decoded = JsonDocument.Parse(gzip);
        Assert.Equal(3000, decoded.RootElement.GetArrayLength());
        Assert.Equal(pages[2999].Caption, decoded.RootElement[2999].GetProperty("caption").GetString());
        Assert.Equal("guild_shop", decoded.RootElement[0].GetProperty("captionSave").GetString());
    }

    [Fact]
    public void HistoryWritesOneEntryPerGroup()
    {
        var entry = new CatalogAdminLogEntry { Id = 8, UserId = 7001, Username = "editor", EntityType = "OFFER", CatalogType = "NORMAL", EntityId = 5,
            Operation = "DELETE", Summary = "Deleted offer", CreatedAt = new DateTime(2026, 10, 4, 12, 0, 0) };
        var packet = new HabbiconTestSupport.RecordingPacket();
        new CatalogStudioHistoryComposer(new(8, 20, [entry])).Compose(packet);
        Assert.Equal(new object[] { 1, 8, 20, 1, 8, 8, 7001, "editor", "Deleted offer", "CATALOG_ADMIN", "2026-10-04T12:00:00", 1, "OFFER", 5, "DELETE" },
            packet.Writes);
    }

    [Fact]
    public void UndoAnswersInTheOperationShapeTheClientWaitsFor()
    {
        var operation = new HabbiconTestSupport.RecordingPacket();
        new CatalogStudioOperationComposer("undo-1", true, "SAVED", "Change undone", 9, [("PAGE", 42)]).Compose(operation);
        Assert.Equal(new object[] { "undo-1", true, "SAVED", "Change undone", 9, 1, "PAGE", 42 }, operation.Writes);
    }
}
