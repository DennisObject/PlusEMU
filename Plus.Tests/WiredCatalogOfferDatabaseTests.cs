using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Catalog;
using Xunit;

namespace Plus.Tests;

public sealed class WiredCatalogDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_WIRED_CATALOG_TEST_CONNECTION_STRING";

    public WiredCatalogDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to a disposable task_wired_catalog_tests_ database.";
    }
}

public sealed class WiredCatalogOfferDatabaseTests : IDisposable
{
    private string? _schema;
    private static string Migration => File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/43_RestoreWiredCatalogOfferIds.sql"));

    [WiredCatalogDatabaseFact]
    public void MissingWiredOffersResolveByTheirAdvertisedIdsAfterRepair()
    {
        using var connection = Open();
        var entries = Offers();
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            connection.Execute("INSERT INTO furniture VALUES (@id, @Name, @Sprite); INSERT INTO catalog_items VALUES (@id, 912363, @item, -1, 3, 1, 0)",
                new { id = i + 1, entry.Name, entry.Sprite, item = (i + 1).ToString() });
        }
        var before = connection.Query<Row>("SELECT * FROM catalog_items ORDER BY id").ToArray();
        var page = Page(connection);
        var index = new CatalogOfferIndex();
        index.Build([page]);
        Assert.All(entries, entry => Assert.False(index.TryGet(entry.Offer, EditorTestSupport.Player(), out _, out _)));

        connection.Execute(Migration);

        var after = connection.Query<Row>("SELECT * FROM catalog_items ORDER BY id").ToArray();
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < entries.Length; i++)
        {
            Assert.Equal(entries[i].Offer, after[i].offer_id);
            Assert.Equal(before[i], after[i] with { offer_id = before[i].offer_id });
        }
        index.Build([Page(connection)]);
        for (var i = 0; i < entries.Length; i++)
        {
            Assert.True(index.TryGet(entries[i].Offer, EditorTestSupport.Player(), out _, out var item));
            Assert.Equal(i + 1, item.Id);
            Assert.Equal(3, item.CostCredits);
        }
        connection.Execute(Migration);
        Assert.Equal(after, connection.Query<Row>("SELECT * FROM catalog_items ORDER BY id").ToArray());
    }

    [WiredCatalogDatabaseFact]
    public void RepairKeepsPositiveIdsWrongSpritesAndOtherProductsClaims()
    {
        using var connection = Open();
        connection.Execute("""
            INSERT INTO furniture VALUES (1,'wf_trg_period_short',14128),(2,'wf_trg_period_long',5042),
              (3,'wf_trg_periodically',3671),(4,'wf_trg_period_short',999),(5,'other_furni',42);
            INSERT INTO catalog_items VALUES (1,1,'1',-1,3,1,0),(2,1,'2',777,9,0,2),
              (3,1,'3',9106,3,1,0),(4,1,'4',-1,3,1,0),(5,1,'5',28195,7,1,0),
              (6,2,'3',-1,3,1,0);
            """);
        var before = connection.Query<Row>("SELECT * FROM catalog_items ORDER BY id").ToArray();

        connection.Execute(Migration);

        var after = connection.Query<Row>("SELECT * FROM catalog_items ORDER BY id").ToArray();
        Assert.Equal(before[..5], after[..5]);
        // A duplicate page for the same product can share its official offer, without stealing another product's ID.
        Assert.Equal(9106, after[5].offer_id);
        Assert.Equal(before[5], after[5] with { offer_id = -1 });
    }

    [Fact]
    public void ReviewedRepeatersExposeTheOfficialSearchOfferIds()
    {
        var entries = Offers();
        Assert.Equal(28195, Assert.Single(entries, e => e.Name == "wf_trg_period_short").Offer);
        Assert.Equal(12506, Assert.Single(entries, e => e.Name == "wf_trg_period_long").Offer);
        Assert.Equal(9106, Assert.Single(entries, e => e.Name == "wf_trg_periodically").Offer);
        Assert.Equal(entries.Length, entries.Select(e => e.Offer).Distinct().Count());
    }

    private static (string Name, int Sprite, int Offer)[] Offers()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Database/WiredCatalog/manifest.json")));
        return manifest.RootElement.GetProperty("entries").EnumerateArray()
            .Where(e => e.TryGetProperty("offer_id", out var offer) && offer.GetInt32() > 0)
            .Select(e => (e.GetProperty("name").GetString()!, e.GetProperty("sprite_id").GetInt32(), e.GetProperty("offer_id").GetInt32())).ToArray();
    }

    private MySqlConnection Open()
    {
        var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable(WiredCatalogDatabaseFactAttribute.Variable));
        if (!builder.Database.StartsWith("task_wired_catalog_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("A disposable Wired catalogue database is required.");
        using (var admin = new MySqlConnection(builder.ConnectionString))
        {
            admin.Open();
            _schema = "task_wired_catalog_tests_" + Guid.NewGuid().ToString("N");
            admin.Execute($"CREATE DATABASE `{_schema}`");
        }
        builder.Database = _schema;
        var connection = new MySqlConnection(builder.ConnectionString);
        connection.Open();
        connection.Execute("""
            CREATE TABLE furniture (id INT PRIMARY KEY, item_name VARCHAR(128) NOT NULL, sprite_id INT NOT NULL) ENGINE=InnoDB;
            CREATE TABLE catalog_items (id INT PRIMARY KEY, page_id INT NOT NULL, item_id VARCHAR(128) NOT NULL,
              offer_id INT NOT NULL, cost_credits INT NOT NULL, offer_active INT NOT NULL, club_level INT NOT NULL) ENGINE=InnoDB;
            """);
        return connection;
    }

    public void Dispose()
    {
        if (_schema == null) return;
        using var admin = new MySqlConnection(Environment.GetEnvironmentVariable(WiredCatalogDatabaseFactAttribute.Variable));
        admin.Open();
        admin.Execute($"DROP DATABASE `{_schema}`");
    }

    private static CatalogPage Page(MySqlConnection connection)
    {
        var page = new CatalogPage { Id = 912363, ParentId = -1, Enabled = true, Visible = true };
        foreach (var row in connection.Query<Row>("SELECT * FROM catalog_items ORDER BY id"))
            page.Items[row.id] = new CatalogItem { Id = row.id, ItemId = uint.Parse(row.item_id), PageId = row.page_id,
                OfferId = row.offer_id, CostCredits = row.cost_credits, ClubLevel = row.club_level };
        return page;
    }

    private sealed record Row(int id, int page_id, string item_id, int offer_id, int cost_credits, int offer_active, int club_level);
}
