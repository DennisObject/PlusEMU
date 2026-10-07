using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Music;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests
{
    public sealed class RoomMusicDatabaseFactAttribute : FactAttribute
    {
        public RoomMusicDatabaseFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROOM_MUSIC_DATABASE"))) {
                Skip = "ROOM_MUSIC_DATABASE is not configured.";
            }
        }
    }

    public sealed class RoomMusicDatabaseTests
    {
        [RoomMusicDatabaseFact]
        public async Task DiscTransferPersistsExactIdentityAndRelogExcludesOnlyLinkedDiscs()
        {
            await using var fixture = await Fixture.Create();
            var store = fixture.Store;
            Assert.Empty(store.Load(30).Tracks);
            var state = store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null)!;
            Assert.True(state.Usable);
            Assert.Equal(31u, Assert.Single(state.Tracks).DiscId);
            Assert.Equal(new uint[] { 32 }, (await new FurnitureInventoryLoader(fixture.Database, fixture.Definitions).Load(7)).Select(item => item.Id));
            Assert.Empty(store.Inventory(7, [31]));
            var second = store.Add(30, 42, 7, state.Version, 32, 100, 0, 10, 0, null)!;
            Assert.Equal(new uint[] { 32, 31 }, second.Tracks.Select(track => track.DiscId));
            Assert.Equal(new uint[] { 32, 31 }, store.Load(30).Tracks.Select(track => track.DiscId));
            var removed = store.Remove(30, 42, 7, second.Version, 1, 0, null)!.Value;
            Assert.Equal(31u, removed.Disc.Id);
            Assert.Equal(7u, removed.Disc.OwnerId);
            Assert.Equal(3u, removed.Disc.UniqueNumber);
            Assert.Equal(30u, removed.Disc.UniqueSeries);
            Assert.Equal("a\nb\nc\nd\ne\nf\n71", removed.Disc.ExtraData.Serialize());
            var loaded = Assert.Single(await new FurnitureInventoryLoader(fixture.Database, fixture.Definitions).Load(7));
            Assert.Equal(31u, loaded.Id);
            Assert.Equal(removed.Disc.ExtraData.Serialize(), loaded.ExtraData.Serialize());
            Assert.Equal(FurniCategory.TraxSong, InventoryItemSnapshot.Capture(loaded).Category);
            Assert.Equal(71, Assert.Single(store.Inventory(7, [31])).Song.Id);
        }

        [RoomMusicDatabaseFact]
        public async Task MissingConfigurationForeignDiscAndConcurrentRepeatedRequestsCannotConsume()
        {
            await using var fixture = await Fixture.Create();
            var store = fixture.Store;
            Assert.Null(store.Add(30, 42, 7, 0, 33, 100, 0, 10, 0, null));
            Assert.Null(store.Add(30, 41, 7, 0, 31, 100, 0, 10, 0, null));
            await fixture.Connection.ExecuteAsync("DELETE FROM room_music_disc_definitions; DELETE FROM room_music_songs");
            Assert.Null(store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null));
            Assert.Equal(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM room_music_players"));
            await fixture.Songs();
            var requests = new[] { Task.Run(() => store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null)),
                Task.Run(() => store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null)) };
            var results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(results.Where(state => state != null));
            Assert.Equal(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM room_music_playlist"));
            Assert.Equal(3, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE id IN(31,32,33)"));
        }

        [RoomMusicDatabaseFact]
        public async Task TriggerFailureRollsBackDiscInsertionOrderingAndPlaybackTogether()
        {
            await using var fixture = await Fixture.Create();
            var store = fixture.Store;
            var first = store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null)!;
            var instant = DateTimeOffset.Parse("2040-02-03T04:05:06.123456Z");
            await fixture.Connection.ExecuteAsync("CREATE TRIGGER fail_music BEFORE INSERT ON room_music_playlist FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced playlist failure'");
            await Assert.ThrowsAsync<MySqlException>(() => Task.Run(() => store.Add(30, 42, 7, first.Version, 32, 100, 0, 10, 1, instant)));
            var unchanged = store.Load(30);
            Assert.Equal(first.Version, unchanged.Version);
            Assert.Equal(first.Tracks.ToArray(), unchanged.Tracks.ToArray());
            Assert.Equal(first.StartedAt, unchanged.StartedAt);
            Assert.Equal("0", await fixture.Connection.ExecuteScalarAsync<string>("SELECT extra_data FROM items WHERE id=30"));
            await fixture.Connection.ExecuteAsync("DROP TRIGGER fail_music");
            var version = store.Playback(30, 42, 7, first.Version, 0, instant);
            Assert.Equal(first.Version + 1, version);
            Assert.Equal(instant, store.Load(30).StartedAt);
            await fixture.Connection.ExecuteAsync("CREATE TRIGGER fail_stop BEFORE UPDATE ON room_music_players FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced stop failure'");
            await Assert.ThrowsAsync<MySqlException>(() => Task.Run(() => new RoomItemPickupStore(fixture.Database).PickUp(new(30, 42, 7, 7, InteractionType.Jukebox, true))));
            Assert.Equal(42u, await fixture.Connection.ExecuteScalarAsync<uint>("SELECT room_id FROM items WHERE id=30"));
            Assert.Equal("1", await fixture.Connection.ExecuteScalarAsync<string>("SELECT extra_data FROM items WHERE id=30"));
            Assert.Equal(instant, store.Load(30).StartedAt);
            await fixture.Connection.ExecuteAsync("DROP TRIGGER fail_stop");
            Assert.False(store.ReturnInvalidPlayer(30, 41, 7));
            Assert.True(store.ReturnInvalidPlayer(30, 42, 7));
            Assert.Null(store.Load(30).StartedAt);
            Assert.Equal(0u, await fixture.Connection.ExecuteScalarAsync<uint>("SELECT room_id FROM items WHERE id=30"));
        }

        [RoomMusicDatabaseFact]
        public async Task NonemptyContainerAndLinkedDiscCannotBeDestroyedAndClearReturnsOriginalOwners()
        {
            await using var fixture = await Fixture.Create();
            var store = fixture.Store;
            var state = store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null)!;
            // The player changed owners through an identity-preserving trade; its first disc did not.
            await fixture.Connection.ExecuteAsync("UPDATE items SET user_id=8 WHERE id=30");
            state = store.Add(30, 42, 8, state.Version, 33, 100, 1, 10, 0, null)!;
            Assert.Equal(new uint[] { 7, 8 }, state.Tracks.Select(track => track.OwnerId));
            await Assert.ThrowsAsync<MySqlException>(() => fixture.Connection.ExecuteAsync("DELETE FROM items WHERE id=30"));
            await Assert.ThrowsAsync<MySqlException>(() => fixture.Connection.ExecuteAsync("DELETE FROM items WHERE id=31"));
            Assert.Equal(2, store.Load(30).Tracks.Length);
            Assert.True(new RoomItemPickupStore(fixture.Database).PickUp(new(30, 42, 8, 8, InteractionType.Jukebox, true)));
            var clear = new InventoryClearStore(fixture.Database, fixture.Definitions);
            await fixture.Connection.ExecuteAsync("CREATE TRIGGER fail_clear BEFORE DELETE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced clear failure'");
            await Assert.ThrowsAsync<MySqlException>(() => Task.Run(() => clear.DeleteAll(8)));
            Assert.Equal(2, store.Load(30).Tracks.Length);
            Assert.Equal(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE id=30"));
            await fixture.Connection.ExecuteAsync("DROP TRIGGER fail_clear");
            var returned = clear.DeleteAll(8);
            Assert.Equal(new uint[] { 31, 33 }, returned.OrderBy(item => item.Id).Select(item => item.Id));
            Assert.Equal(new uint[] { 7, 8 }, returned.OrderBy(item => item.Id).Select(item => item.OwnerId));
            Assert.Equal(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM room_music_playlist"));
            Assert.Equal(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE id=30"));
            Assert.Equal(new uint[] { 31, 32 }, (await new FurnitureInventoryLoader(fixture.Database, fixture.Definitions).Load(7)).OrderBy(item => item.Id).Select(item => item.Id));
            Assert.Equal(33u, Assert.Single(await new FurnitureInventoryLoader(fixture.Database, fixture.Definitions).Load(8)).Id);
        }

        [RoomMusicDatabaseFact]
        public async Task MarketplaceRefusesLinkedDiscAndNonemptyPlayerWithoutAnOffer()
        {
            await using var fixture = await Fixture.Create();
            var state = fixture.Store.Add(30, 42, 7, 0, 31, 100, 0, 10, 0, null)!;
            Assert.True(new RoomItemPickupStore(fixture.Database).PickUp(new(30, 42, 7, 7, InteractionType.Jukebox, true)));
            var offers = new Plus.HabboHotel.Catalog.Marketplace.MarketplaceOfferStore(fixture.Database);
            var clock = DateTimeOffset.Parse("2040-02-03T04:05:06Z");
            Plus.HabboHotel.Catalog.Marketplace.MarketplaceListing Listing(uint id, uint definition) => new(id, definition, 7, 10, 11, "music", 1, "1", clock, "0", 0, 0);
            Assert.False(offers.ListFurni(Listing(31, 100)));
            Assert.False(offers.ListFurni(Listing(30, 101)));
            Assert.Equal(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM catalog_marketplace_offers"));
            Assert.Equal(2, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE id IN(30,31)"));
            Assert.NotNull(fixture.Store.Remove(30, 0, 7, state.Version + 1, 0, 0, null));
            Assert.True(offers.ListFurni(Listing(30, 101)));
            Assert.Equal(1, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM catalog_marketplace_offers"));
            Assert.Equal(0, await fixture.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM items WHERE id=30"));
            Assert.NotNull(fixture.Store.AvailableDisc(31, 7));
        }

        internal sealed class Fixture : IAsyncDisposable
        {
            private readonly MySqlConnection _server;
            private readonly string _schema;
            public MySqlConnection Connection { get; }
            public IDatabase Database { get; }
            public Definitions Definitions { get; } = new();
            public RoomMusicStore Store => new(Database, Definitions);
            private Fixture(MySqlConnection server, MySqlConnection connection, string schema)
            {
                _server = server;
                Connection = connection;
                _schema = schema;
                Database = new TestDatabase(connection.ConnectionString);
            }
            internal static string SchemaSql => File.ReadAllText(Path.Combine(Root(), "Resources", "SQLs", "Updates", "55_RoomMusic.sql"));
            public static async Task<Fixture> Create()
            {
                SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
                var schema = "room_music_" + Guid.NewGuid().ToString("N");
                var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_MUSIC_DATABASE"))
                { Database = "", Pooling = false, AllowZeroDateTime = true, ConvertZeroDateTime = true };
                var server = new MySqlConnection(builder.ConnectionString);
                await server.OpenAsync();
                await server.ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci");
                builder.Database = schema;
                var connection = new MySqlConnection(builder.ConnectionString);
                var fixture = new Fixture(server, connection, schema);

                try {
                    await connection.OpenAsync();
                    await connection.ExecuteAsync("CREATE TABLE users(id INT PRIMARY KEY,username VARCHAR(25)); INSERT INTO users VALUES(7,'owner'),(8,'other'); CREATE TABLE items_groups(id INT UNSIGNED PRIMARY KEY,group_id INT)");
                    var dump = File.ReadAllText(Path.Combine(Root(), "Resources", "SQLs", "Original Database.sql"));
                    var start = dump.IndexOf("CREATE TABLE `items`", StringComparison.Ordinal);
                    await connection.ExecuteAsync(dump[start..(dump.IndexOf(';', start) + 1)]);
                    var offerStart = dump.IndexOf("CREATE TABLE `catalog_marketplace_offers`", StringComparison.Ordinal);
                    await connection.ExecuteAsync(dump[offerStart..(dump.IndexOf(';', offerStart) + 1)]);
                    var migration = SchemaSql;
                    await connection.ExecuteAsync(migration);
                    await connection.ExecuteAsync(migration);
                    await connection.ExecuteAsync("""
                        INSERT INTO items(id,user_id,room_id,base_item,extra_data,limited_number,limited_stack) VALUES
                        (30,7,42,101,'0',0,0),(31,7,0,100,'a\nb\nc\nd\ne\nf\n71',3,30),(32,7,0,100,'72',0,0),(33,8,0,100,'71',0,0);
                        """);
                    await fixture.Songs();

                    return fixture;
                }
                catch {
                    await fixture.DisposeAsync();
                    throw;
                }
            }
            public Task Songs() => Connection.ExecuteAsync("INSERT INTO room_music_songs VALUES(71,'code71','one','author','1,1,1',1000),(72,'code72','two','author','1,2,2',2000); INSERT INTO room_music_disc_definitions VALUES(100,71)");
            public async ValueTask DisposeAsync()
            {
                await Connection.DisposeAsync();

                try {
                    await _server.ExecuteAsync($"DROP DATABASE IF EXISTS `{_schema}`");
                }
                finally {
                    await _server.DisposeAsync();
                }
            }
            private static string Root()
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);

                while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Plus Emulator.csproj"))) {
                    directory = directory.Parent;
                }

                return directory?.FullName ?? throw new InvalidOperationException("Repository root unavailable.");
            }
        }
        internal sealed class Definitions : IItemDataManager
        {
            public Dictionary<uint, ItemDefinition> Items { get; } = new()
            {
                [100] = new() { Id = 100, Type = ItemType.Floor, InteractionType = InteractionType.MusicDisc },
                [101] = new() { Id = 101, Type = ItemType.Floor, InteractionType = InteractionType.Jukebox, ItemName = "jukebox*1" }
            };
            public Dictionary<int, uint> Gifts { get; } = [];
            public void Init()
            {
            }
            public ItemDefinition? GetItemByName(string name) => Items.Values.FirstOrDefault(item => item.ItemName == name);
        }
        private sealed class TestDatabase(string connectionString) : IDatabase
        {
            public bool IsConnected() => true;
            public IDbConnection Connection() => new MySqlConnection(connectionString);
        }
    }
}
