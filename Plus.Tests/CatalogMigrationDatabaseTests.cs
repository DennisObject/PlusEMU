using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Items;
using Xunit;

namespace Plus.Tests;

// Runs Database/Migrations/52_NormalizeCatalog.sql on a small catalog in the shape it had before, then loads the result.
public sealed class CatalogMigrationDatabaseTests
{
    private const int Custom = CatalogOfferIndex.CustomOfferIdBase;

    [RoomComponentDatabaseFact]
    public async Task CatalogRowsBecomeOffersWithOneIdEach()
    {
        await InSchema(async (connection, database) =>
        {
            CreateLegacyCatalog(connection);
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/52_NormalizeCatalog.sql")));

            // Pages a root cannot reach are gone; NULL is the root and an empty link is no link.
            Assert.Equal([(1, (int?)null, "root"), (2, 1, null), (5, null, "staff")],
                connection.Query<(int, int?, string?)>("SELECT id, parent_id, link FROM catalog_pages ORDER BY id"));
            Assert.Equal(["head", "teaser"], connection.Query<string>("SELECT image FROM catalog_page_images WHERE page_id = 1 ORDER BY slot"));
            Assert.Equal(["a", "", "c"], connection.Query<string>("SELECT text FROM catalog_page_texts WHERE page_id = 1 ORDER BY slot"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_page_texts WHERE page_id = 2"));

            // Page 1 keeps its order. The newest row naming official offer 500 keeps it; the identical chairs on pages 1
            // and 2 become one offer, numbered from their first row.
            Assert.Equal([Custom + 100, 500, Custom + 103, Custom + 104, Custom + 105, Custom + 106, Custom + 107, Custom + 108, Custom + 113, Custom + 114],
                connection.Query<int>("SELECT offer_id FROM catalog_page_offers WHERE page_id = 1 ORDER BY position"));
            Assert.Equal([1, 2], connection.Query<int>($"SELECT page_id FROM catalog_page_offers WHERE offer_id = {Custom + 100} ORDER BY page_id"));
            Assert.Equal(("table", 7), connection.QuerySingle<(string, int)>("SELECT localization_key, cost_credits FROM catalog_offers WHERE id = 500"));
            Assert.Equal(10, connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_offers"));

            var products = connection.Query<(int Offer, int Position, string Type, uint? Furniture, int? Effect, string? Badge, int? Bot, int? Pet, int? Habbicon, int Amount, string Extra)>("""
                SELECT offer_id, position, product_type, furniture_id, effect_id, badge_code, bot_preset_id, pet_type, habbicon_id, amount, extra_param
                FROM catalog_offer_products ORDER BY offer_id, position
                """).ToList();
            Assert.Contains((Custom + 103, 0, "furni", (uint?)12, (int?)null, (string?)null, (int?)null, (int?)null, (int?)null, 1, "101"), products);
            Assert.Contains((Custom + 104, 0, "effect", (uint?)null, (int?)22, (string?)null, (int?)null, (int?)null, (int?)null, 1, ""), products);
            Assert.Contains((Custom + 105, 0, "badge", (uint?)null, (int?)null, "ADM", (int?)null, (int?)null, (int?)null, 1, ""), products);
            Assert.Contains((Custom + 106, 0, "bot", (uint?)null, (int?)null, (string?)null, (int?)15, (int?)null, (int?)null, 1, ""), products);
            Assert.Contains((Custom + 107, 0, "pet", (uint?)null, (int?)null, (string?)null, (int?)null, (int?)5, (int?)null, 1, ""), products);
            Assert.Contains((Custom + 113, 0, "habbicon", (uint?)null, (int?)null, (string?)null, (int?)null, (int?)null, (int?)61, 1, ""), products);
            // A deal sells its listed furniture, after the badge it gives.
            Assert.Equal([(0, "badge", (uint?)null, 1), (1, "furni", 10u, 2), (2, "furni", 11u, 3)],
                products.Where(p => p.Offer == Custom + 108).Select(p => (p.Position, p.Type, p.Furniture, p.Amount)));
            Assert.Equal(["ADM", "XYZ"], connection.Query<string>("SELECT code FROM badge_definitions ORDER BY code"));

            Assert.Equal((0, 1), connection.QuerySingle<(int, int)>($"SELECT enabled, 1 - bulk_purchase FROM catalog_offers WHERE id = {Custom + 113}"));
            Assert.Equal((5u, 2u), connection.QuerySingle<(uint, uint)>($"SELECT stack, sold FROM catalog_offer_limited WHERE offer_id = {Custom + 114}"));
            Assert.Equal(Custom + 100, connection.QuerySingle<int>("SELECT offer_id FROM club_gift_offers"));
            Assert.Equal(Custom + 100, connection.QuerySingle<int>("SELECT offer_id FROM club_gift_claims"));
            Assert.Equal(500, connection.QuerySingle<int>("SELECT entity_id FROM catalog_admin_log"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name IN ('catalog_items', 'catalog_deals')"));

            // New offers without an official id are numbered after the migrated ones.
            Assert.Equal(Custom + 115, connection.QuerySingle<int>("INSERT INTO catalog_offers (localization_key) VALUES ('new'); SELECT CAST(LAST_INSERT_ID() AS SIGNED)"));
            connection.Execute($"DELETE FROM catalog_offers WHERE id = {Custom + 115}");
            Assert.Throws<MySqlException>(() => connection.Execute("INSERT INTO catalog_offer_products (offer_id, position, product_type) VALUES (500, 9, 'furni')"));

            var catalog = new CatalogManager(null!, null!, null!, database, TestLogging.For<CatalogManager>(), Items());
            await catalog.Start();

            Assert.True(catalog.TryGetPage(1, out var page));
            Assert.Equal(["head", "teaser"], page.Images);
            Assert.Equal(10, page.Offers.Count);
            Assert.True(catalog.TryGetPage(2, out var other));
            Assert.Same(page.Offers[Custom + 100], other.Offers[Custom + 100]);
            Assert.True(page.Offers[Custom + 108].Products is [{ Type: CatalogProductType.Badge, BadgeCode: "XYZ" }, { Amount: 2 }, { Amount: 3 }]);
            Assert.True(page.Offers[Custom + 114] is { IsLimited: true, LimitedStack: 5, LimitedSells: 2 });
            Assert.True(catalog.TryGetOffer(500, EditorTestSupport.Player(), out _, out var table));
            Assert.Equal("table", table.Definition!.ItemName);
        });
    }

    [RoomComponentDatabaseFact]
    public async Task OffersPricedInAnyActivityPointTypeLoad()
    {
        await InSchema(async (connection, database) =>
        {
            CreateLegacyCatalog(connection);
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/52_NormalizeCatalog.sql")));
            connection.Execute("UPDATE catalog_offers SET cost_points = 12, points_type = 101 WHERE id = 500");

            var catalog = new CatalogManager(null!, null!, null!, database, TestLogging.For<CatalogManager>(), Items());
            await catalog.Start();

            Assert.True(catalog.TryGetOffer(500, EditorTestSupport.Player(), out _, out var offer));
            Assert.Equal((7, 12, 101), (offer.CostCredits, offer.CostPoints, offer.PointsType));
        });
    }

    private static void CreateLegacyCatalog(MySqlConnection connection) => connection.Execute("""
        CREATE TABLE acl_permissions (`key` VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY) ENGINE=InnoDB;
        CREATE TABLE furniture (id INT UNSIGNED NOT NULL PRIMARY KEY, item_name VARCHAR(70) NOT NULL, sprite_id INT NOT NULL DEFAULT 0,
            type ENUM('s','i','e','h','v','r','b','p') NOT NULL DEFAULT 's', interaction_type VARCHAR(25) NOT NULL DEFAULT 'default',
            behaviour_data INT NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=latin1;
        CREATE TABLE badge_definitions (code VARCHAR(35) NOT NULL PRIMARY KEY, required_right VARCHAR(191) NOT NULL DEFAULT '')
            ENGINE=InnoDB DEFAULT CHARSET=utf8mb3 COLLATE=utf8mb3_general_ci;
        CREATE TABLE catalog_bot_presets (id INT NOT NULL PRIMARY KEY, name VARCHAR(255) NOT NULL DEFAULT '', figure VARCHAR(255) NOT NULL DEFAULT '',
            gender VARCHAR(255) NOT NULL DEFAULT 'M', motto VARCHAR(255) NOT NULL DEFAULT '', ai_type VARCHAR(16) NOT NULL DEFAULT 'generic') ENGINE=InnoDB;
        CREATE TABLE habbicons (id INT NOT NULL PRIMARY KEY) ENGINE=InnoDB;
        CREATE TABLE catalog_pages (id INT NOT NULL AUTO_INCREMENT, parent_id INT NOT NULL DEFAULT -1, caption VARCHAR(128) NOT NULL,
            icon_image INT NOT NULL DEFAULT 1, required_permission VARCHAR(191) CHARACTER SET ascii COLLATE ascii_bin NULL, order_num INT NOT NULL,
            page_link VARCHAR(128) NOT NULL DEFAULT '', page_layout VARCHAR(64) NOT NULL DEFAULT 'default_3x3', page_strings_1 TEXT NOT NULL,
            page_strings_2 TEXT NOT NULL, visible BIT(1) NOT NULL DEFAULT b'1', enabled BIT(1) NOT NULL DEFAULT b'1', required_club_level INT NOT NULL DEFAULT 0,
            PRIMARY KEY (id), UNIQUE KEY id (id), KEY order_num (order_num)) ENGINE=InnoDB DEFAULT CHARSET=latin1;
        CREATE TABLE catalog_items (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, page_id INT NOT NULL, item_id VARCHAR(120) NOT NULL,
            catalog_name VARCHAR(100) NOT NULL, cost_credits INT NOT NULL DEFAULT 3, cost_pixels INT NOT NULL DEFAULT 0, cost_diamonds INT NOT NULL DEFAULT 0,
            amount INT NOT NULL DEFAULT 1, limited_sells INT NOT NULL DEFAULT 0, limited_stack INT NOT NULL DEFAULT 0, offer_active TINYINT(1) NOT NULL DEFAULT 1,
            extradata VARCHAR(1024) NOT NULL DEFAULT '', badge VARCHAR(64) NOT NULL DEFAULT '', offer_id INT NOT NULL DEFAULT -1,
            habbicon_id INT NOT NULL DEFAULT 0, club_level TINYINT UNSIGNED NOT NULL DEFAULT 0, preview_image VARCHAR(255) NOT NULL DEFAULT '',
            order_num INT NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=latin1;
        CREATE TABLE catalog_deals (id INT NOT NULL PRIMARY KEY, items TEXT NOT NULL, name VARCHAR(35) NOT NULL, room_id INT NOT NULL) ENGINE=InnoDB;
        CREATE TABLE club_gift_offers (catalog_item_id INT NOT NULL PRIMARY KEY, days_required INT NOT NULL DEFAULT 0, enabled TINYINT(1) NOT NULL DEFAULT 1) ENGINE=InnoDB;
        CREATE TABLE club_gift_claims (id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id INT NOT NULL, gift_number INT NOT NULL,
            catalog_item_id INT NOT NULL, claimed_at DATETIME(6) NULL) ENGINE=InnoDB;
        CREATE TABLE catalog_admin_log (id INT NOT NULL AUTO_INCREMENT PRIMARY KEY, entity_type VARCHAR(16) NOT NULL, entity_id INT NOT NULL) ENGINE=InnoDB;
        CREATE TABLE catalog_promotions (id INT NOT NULL PRIMARY KEY, title VARCHAR(35), image VARCHAR(75), unknown INT, page_link VARCHAR(35),
            parent_id INT, position INT NOT NULL DEFAULT 0, item_type TINYINT NOT NULL DEFAULT 0, offer_id INT NOT NULL DEFAULT -1,
            product_code VARCHAR(128) NOT NULL DEFAULT '', expires_at DATETIME(6) NULL) ENGINE=InnoDB;
        CREATE TABLE catalog_club_offers (id INT PRIMARY KEY, name VARCHAR(64), days INT, credits INT, points INT, points_type INT, giftable BOOL, enabled BOOL) ENGINE=InnoDB;

        INSERT INTO acl_permissions VALUES ('catalog.pages.staff');
        INSERT INTO furniture (id, item_name, sprite_id, type, interaction_type, behaviour_data) VALUES
            (9, 'ltd_chair', 9, 's', 'default', 0), (10, 'chair', 10, 's', 'default', 0), (11, 'table', 11, 's', 'Default', 0),
            (12, 'wallpaper', 12, 'i', 'wallpaper', 0), (13, 'avatar_effect22', 22, 'e', 'default', 0), (14, 'ADM', 0, 'b', 'badge', 0),
            (15, 'bot_generic', 0, 'r', 'default', 0), (16, 'a0 pet5', 1532, 'p', 'pet', 5), (17, 'deal_bundle', 17, 's', 'deal', 1),
            (18, 'DEAL_HC_1', 0, 'h', 'default', 0);
        INSERT INTO catalog_bot_presets (id) VALUES (15);
        INSERT INTO habbicons VALUES (61);
        INSERT INTO catalog_deals VALUES (1, '10*2;11*3;404*1', 'bundle', 0);
        INSERT INTO catalog_pages (id, parent_id, caption, required_permission, order_num, page_link, page_layout, page_strings_1, page_strings_2) VALUES
            (1, -1, 'Root', NULL, 0, 'root', 'default_3x3', 'head|teaser', 'a||c'),
            (2, 1, 'Child', NULL, 0, '', 'default_3x3', '', ' '),
            (3, 999, 'Orphan', NULL, 0, 'orphan', 'default_3x3', '', ''),
            (4, 3, 'Below orphan', NULL, 0, 'below', 'default_3x3', '', ''),
            (5, -1, 'Staff', 'catalog.pages.staff', 1, 'staff', 'vip_buy', '', '');
        INSERT INTO catalog_items (id, page_id, item_id, catalog_name, cost_credits, amount, offer_id, order_num, badge, offer_active, habbicon_id, limited_stack, limited_sells) VALUES
            (100, 1, '10', 'chair', 3, 1, 500, 0, '', 1, 0, 0, 0),
            (101, 2, '10', 'chair', 3, 1, 500, 0, '', 1, 0, 0, 0),
            (102, 1, '11', 'table', 7, 1, 500, 0, '', 1, 0, 0, 0),
            (103, 1, '12\r\n', 'wallpaper_single_101', 3, 1, -1, 0, '', 1, 0, 0, 0),
            (104, 1, '13', 'avatar_effect22', 3, 1, -1, 0, '', 1, 0, 0, 0),
            (105, 1, '14', 'ADM', 3, 1, -1, 0, '', 1, 0, 0, 0),
            (106, 1, '15', 'bot_generic', 3, 1, -1, 0, '', 1, 0, 0, 0),
            (107, 1, '16', 'a0 pet5', 3, 1, -1, 0, '', 1, 0, 0, 0),
            (108, 1, '17', 'deal_bundle', 3, 1, -1, 0, 'XYZ', 1, 0, 0, 0),
            (109, 5, '18', 'DEAL_HC_1', 3, 1, -1, 0, '', 1, 0, 0, 0),
            (110, 3, '10', 'chair', 3, 1, -1, 0, '', 1, 0, 0, 0),
            (111, 998, '10', 'chair', 3, 1, -1, 0, '', 1, 0, 0, 0),
            (112, 1, '10', 'chair', 3, 0, -1, 0, '', 1, 0, 0, 0),
            (113, 1, '0', 'toast', 5, 1, -1, 0, '', 0, 61, 0, 0),
            (114, 1, '09', 'ltd_chair', 3, 1, -1, 0, '', 1, 0, 5, 2);
        INSERT INTO club_gift_offers VALUES (100, 0, 1);
        INSERT INTO club_gift_claims (user_id, gift_number, catalog_item_id) VALUES (7, 1, 100);
        INSERT INTO catalog_admin_log (entity_type, entity_id) VALUES ('OFFER', 102);
        """);

    private static IItemDataManager Items()
    {
        var items = new[] { (9u, "ltd_chair"), (10u, "chair"), (11u, "table"), (12u, "wallpaper") }
            .ToDictionary(item => item.Item1, item => new ItemDefinition { Id = item.Item1, ItemName = item.Item2, SpriteId = (int)item.Item1, ProductType = "s" });

        return CatalogSnapshotTestSupport.Proxy<IItemDataManager>((method, _) => method == "get_Items" ? items : throw new InvalidOperationException(method));
    }

    private static async Task InSchema(Func<MySqlConnection, IDatabase, Task> run)
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!) { AllowUserVariables = true };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_catalog_migration_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");

        try {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            await run(connection, new ProbeDatabase(options.ConnectionString));
        }
        finally {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
