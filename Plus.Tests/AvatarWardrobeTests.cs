using System.Collections.Immutable;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Avatar;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Avatar;
using Plus.Core.FigureData;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Clothing;
using Plus.HabboHotel.Users.UserData;
using Xunit;

namespace Plus.Tests;

public class AvatarWardrobeTests
{
    [Fact]
    public async Task SaveEventDecodesSlotLookAndGenderOnly()
    {
        var wardrobe = new RecordingWardrobe();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });

        await new SaveWardrobeOutfitEvent(wardrobe).Parse(client, HabbiconTestSupport.Incoming(3, "lg-1-2", "m"));

        Assert.Equal(new object[] { 3, "lg-1-2", "m" }, Assert.Single(wardrobe.Saves));
        Assert.Equal(0, wardrobe.ShowCount);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task GetEventOnlyDelegatesToTheWardrobeService()
    {
        var wardrobe = new RecordingWardrobe();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });

        await new GetWardrobeEvent(wardrobe).Parse(client, HabbiconTestSupport.Incoming());

        Assert.Equal(1, wardrobe.ShowCount);
        Assert.Empty(wardrobe.Saves);
        Assert.Empty(sent);
    }

    [Fact]
    public void ComposerWritesTheExactWardrobeOrder()
    {
        var snapshot = new WardrobeSnapshot(ImmutableArray.Create(new WardrobeSlot(1, "a", "F"), new WardrobeSlot(2, "b", "M")));
        var packet = new HabbiconTestSupport.RecordingPacket();

        new WardrobeComposer(snapshot).Compose(packet);

        Assert.Equal(new object[] { 1, 2, 1, "a", "F", 2, "b", "M" }, packet.Writes);
    }

    [Fact]
    public void ComposerFreezesSlotsAndUpperCasesGenderBeforeComposing()
    {
        var source = new List<WardrobeSlot> { new(5, "x", "f") };
        var composer = new WardrobeComposer(new WardrobeSnapshot(source.ToImmutableArray()));
        var first = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(first);

        source.Clear();
        source.Add(new WardrobeSlot(9, "changed", "m"));
        var second = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(second);

        Assert.Equal(new object[] { 1, 1, 5, "x", "F" }, first.Writes);
        Assert.Equal(first.Writes, second.Writes);
    }

    [Fact]
    public async Task SaveProcessesTheFigureForClubLevelAndStoresNormalizedGender()
    {
        var figures = new RecordingFigures();
        var store = new RecordingStore();
        var habbo = new Habbo { Id = 7, Clothing = new ClothingComponent() };
        var service = new AvatarWardrobeService(figures, store, UserDataExists(true));

        service.SaveOutfit(habbo, 3, "raw-look", "m");

        Assert.Equal(new object[] { "raw-look", "m", ClubAccess.LevelFor(habbo.Access) }, Assert.Single(figures.Calls));
        Assert.Equal(new object[] { 7, 3, "processed", "M" }, Assert.Single(store.Saves));
    }

    [Fact]
    public void OutfitIsNotSavedWithoutALoadedWardrobe()
    {
        var figures = new RecordingFigures();
        var store = new RecordingStore();
        var habbo = new Habbo { Id = 7 };

        new AvatarWardrobeService(figures, store, UserDataExists(true)).SaveOutfit(habbo, 3, "raw-look", "m");

        Assert.Empty(figures.Calls);
        Assert.Empty(store.Saves);
    }

    [Fact]
    public void FigureChangesWithoutALoadedWardrobeLeaveTheLookAndStorageUntouched()
    {
        var calls = new List<string>();
        T Untouched<T>() where T : class => CatalogSnapshotTestSupport.Proxy<T>((method, _) =>
        {
            calls.Add($"{typeof(T).Name}.{method}");

            throw new InvalidOperationException(method);
        });
        var figures = new RecordingFigures();
        var service = new UserProfileService(figures, Untouched<Plus.HabboHotel.Achievements.IAchievementManager>(),
            Untouched<Plus.HabboHotel.Quests.IQuestManager>(), Untouched<Plus.HabboHotel.Rooms.Chat.Filter.IWordFilterManager>(),
            Untouched<Plus.Database.IDatabase>(), TimeProvider.System, Untouched<Plus.HabboHotel.Rooms.Chat.Styles.IChatStyleManager>(),
            Untouched<Plus.HabboHotel.Quests.IRewardTrackManager>(), Untouched<Plus.HabboHotel.Users.Authentication.IAccountSessionGate>(),
            Untouched<Plus.Core.Settings.ISettingsManager>());
        var habbo = new Habbo { Id = 7, Look = "hd-180-1", Gender = "M" };
        var (client, sent) = HabbiconTestSupport.Client(habbo);

        service.UpdateFigure(client, new FigureUpdateRequest("F", "hd-600-1.ch-3001-1"));
        service.ApplyMannequin(client, new FigureUpdateRequest("F", "hd-600-1.ch-3001-1"));

        Assert.Empty(calls);
        Assert.Empty(figures.Calls);
        Assert.Equal("hd-180-1", habbo.Look);
        Assert.Equal("M", habbo.Gender);
        Assert.Empty(sent);
    }

    [Fact]
    public void FigureFailureWritesNothing()
    {
        var store = new RecordingStore();
        var habbo = new Habbo { Id = 7, Clothing = new ClothingComponent() };
        var service = new AvatarWardrobeService(new RecordingFigures { Failure = new InvalidOperationException("figure") }, store, UserDataExists(true));

        Assert.Throws<InvalidOperationException>(() => service.SaveOutfit(habbo, 3, "raw-look", "m"));

        Assert.Empty(store.Saves);
    }

    [Fact]
    public void StoreFailureSurfacesWithoutPublishing()
    {
        var habbo = new Habbo { Id = 7, Clothing = new ClothingComponent() };
        var service = new AvatarWardrobeService(new RecordingFigures(), new RecordingStore { Failure = new InvalidOperationException("store") }, UserDataExists(true));

        Assert.Throws<InvalidOperationException>(() => service.SaveOutfit(habbo, 3, "raw-look", "m"));
    }

    [Fact]
    public async Task ShowWardrobeSendsNothingForMissingAccounts()
    {
        var store = new RecordingStore();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var service = new AvatarWardrobeService(new RecordingFigures(), store, UserDataExists(false));

        await service.ShowWardrobe(client);

        Assert.Empty(sent);
        Assert.Equal(0, store.LoadCount);
    }

    [Fact]
    public async Task ShowWardrobeSendsStoredSlotsAsOneComposer()
    {
        var store = new RecordingStore { Slots = ImmutableArray.Create(new WardrobeSlot(4, "look4", "M")) };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var service = new AvatarWardrobeService(new RecordingFigures(), store, UserDataExists(true));

        await service.ShowWardrobe(client);

        var packet = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.WardrobeComposer, packet.Header);
        var reader = new FlashIncomingPacket { Buffer = packet.Payload };
        Assert.Equal((1, 1, 4), (reader.ReadInt(), reader.ReadInt(), reader.ReadInt()));
        Assert.Equal(("look4", "M"), (reader.ReadString(), reader.ReadString()));
    }

    [WardrobeDatabaseFact]
    public async Task RepeatedSameSlotSaveUpdatesAndReloadsThroughTheProductionStore()
    {
        await WithSchema(async connectionString =>
        {
            var service = new AvatarWardrobeService(new RecordingFigures { Echo = true }, new AvatarWardrobeStore(new HabbiconDatabaseTests.TestDatabase(connectionString)), UserDataExists(true));
            var habbo = new Habbo { Id = 7, Clothing = new ClothingComponent() };

            service.SaveOutfit(habbo, 1, "look1", "m");
            service.SaveOutfit(habbo, 1, "look2", "f");
            service.SaveOutfit(habbo, 2, "look3", "m");

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_wardrobe WHERE user_id = 7"));
            Assert.Equal(("look2", "F"), connection.QuerySingle<(string, string)>("SELECT look, gender FROM user_wardrobe WHERE user_id = 7 AND slot_id = 1"));

            var (client, sent) = HabbiconTestSupport.Client(habbo);
            await service.ShowWardrobe(client);
            var reader = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
            Assert.Equal((1, 2), (reader.ReadInt(), reader.ReadInt()));
            var loaded = new Dictionary<int, (string Look, string Gender)>();

            for (int i = 0; i < 2; i++) {
                var slot = reader.ReadInt();
                loaded[slot] = (reader.ReadString(), reader.ReadString());
            }

            Assert.Equal(("look2", "F"), loaded[1]);
            Assert.Equal(("look3", "M"), loaded[2]);
        });
    }

    [WardrobeDatabaseFact]
    public async Task OversizedLookFailsWithoutAnyPartialWrite()
    {
        await WithSchema(async connectionString =>
        {
            var service = new AvatarWardrobeService(new RecordingFigures { Echo = true }, new AvatarWardrobeStore(new HabbiconDatabaseTests.TestDatabase(connectionString)), UserDataExists(true));
            var habbo = new Habbo { Id = 7, Clothing = new ClothingComponent() };
            service.SaveOutfit(habbo, 1, "kept", "m");

            Assert.Throws<MySqlException>(() => service.SaveOutfit(habbo, 4, new string('x', 130), "m"));

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_wardrobe WHERE slot_id = 4"));
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_wardrobe WHERE user_id = 7"));
        });
    }

    [WardrobeDatabaseFact]
    public async Task MissingAccountSaveWritesNothingThroughTheProductionStore()
    {
        await WithSchema(async connectionString =>
        {
            var store = new AvatarWardrobeStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            Assert.Throws<InvalidOperationException>(() => store.SaveSlot(99, 1, "orphan", "m"));

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_wardrobe"));
        });
    }

    [WardrobeDatabaseFact]
    public async Task ConcurrentSameSlotSavesYieldOneRowWithoutAUniqueKey()
    {
        await WithSchema(async connectionString =>
        {
            var database = new HabbiconDatabaseTests.TestDatabase(connectionString);
            var store = new AvatarWardrobeStore(database);

            await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() => store.SaveSlot(7, 5, $"look{index}", "m"))));

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_wardrobe WHERE user_id = 7 AND slot_id = 5"));
            Assert.Equal("M", connection.ExecuteScalar<string>("SELECT gender FROM user_wardrobe WHERE user_id = 7 AND slot_id = 5"));
        });
    }

    [WardrobeDatabaseFact]
    public async Task ExistingDuplicateRowsAreUpdatedTogetherAndNotMerged()
    {
        await WithSchema(async connectionString =>
        {
            using (var connection = new MySqlConnection(connectionString)) {
                connection.Open();
                connection.Execute("INSERT INTO user_wardrobe (user_id, slot_id, look, gender) VALUES (7, 6, 'old-a', 'M'), (7, 6, 'old-b', 'M')");
            }

            var store = new AvatarWardrobeStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            store.SaveSlot(7, 6, "new", "f");

            using var verify = new MySqlConnection(connectionString);
            verify.Open();
            Assert.Equal(2, verify.ExecuteScalar<int>("SELECT COUNT(*) FROM user_wardrobe WHERE user_id = 7 AND slot_id = 6"));
            Assert.Equal(2, verify.ExecuteScalar<int>("SELECT COUNT(*) FROM user_wardrobe WHERE user_id = 7 AND slot_id = 6 AND look = 'new' AND gender = 'F'"));
        });
    }

    private static IUserDataFactory UserDataExists(bool exists) =>
        CatalogSnapshotTestSupport.Proxy<IUserDataFactory>((_, _) => Task.FromResult(exists));

    private static async Task WithSchema(Func<string, Task> body)
    {
        var server = Environment.GetEnvironmentVariable("PLUS_WARDROBE_TEST_CONNECTION_STRING")!;
        var schema = "task_wardrobe_tests_" + Guid.NewGuid().ToString("N")[..12];
        var options = new MySqlConnectionStringBuilder(server) { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true };

        using (var admin = new MySqlConnection(server)) {
            admin.Open();
            admin.Execute($"CREATE DATABASE `{schema}`");
        }

        try {
            using (var connection = new MySqlConnection(options.ConnectionString)) {
                connection.Open();
                connection.Execute("CREATE TABLE users (id INT UNSIGNED PRIMARY KEY, username VARCHAR(32) NOT NULL DEFAULT '') ENGINE=InnoDB");
                connection.Execute("INSERT INTO users (id, username) VALUES (7, 'wardrobe_tests')");
                connection.Execute(WardrobeTableDdl());
            }

            await body(options.ConnectionString);
        }
        finally {
            using var admin = new MySqlConnection(server);
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    // The user_wardrobe definition comes from the pristine dump, so the probe cannot drift from the shipped table.
    private static string WardrobeTableDdl()
    {
        var dump = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
        int start = dump.IndexOf("CREATE TABLE `user_wardrobe`", StringComparison.Ordinal);
        int end = dump.IndexOf("ENGINE=InnoDB", start, StringComparison.Ordinal) + "ENGINE=InnoDB DEFAULT CHARSET=latin1;".Length;

        return dump[start..end];
    }

    private sealed class RecordingWardrobe : IAvatarWardrobeService
    {
        public List<object[]> Saves { get; } = new();
        public int ShowCount { get; private set; }
        public Task ShowWardrobe(GameClient session)
        {
            ShowCount++;

            return Task.CompletedTask;
        }
        public void SaveOutfit(Habbo habbo, int slotId, string look, string gender) => Saves.Add(new object[] { slotId, look, gender });
    }

    private sealed class RecordingFigures : IFigureDataManager
    {
        public List<object[]> Calls { get; } = new();
        public Exception? Failure { get; init; }
        // Database tests need the figure to pass through unchanged so stored rows can be checked against the input.
        public bool Echo { get; init; }
        public void Init() { }
        public string ProcessFigure(string figure, string gender, ICollection<Plus.HabboHotel.Users.Clothing.Parts.ClothingParts>? clothingParts, int clubLevel)
        {
            Calls.Add(new object[] { figure, gender, clubLevel });

            if (Failure != null) {
                throw Failure;
            }

            return Echo ? figure : "processed";
        }
        public Plus.Core.FigureData.Types.Palette? GetPalette(int colorId) => null;
        public bool TryGetPalette(int palletId, out Plus.Core.FigureData.Types.Palette? palette)
        {
            palette = null;

            return false;
        }
        public int GetRandomColor(int palletId, int clubLevel = 0) => 0;
        public string FilterFigure(string figure) => figure;
    }

    private sealed class RecordingStore : IAvatarWardrobeStore
    {
        public List<object[]> Saves { get; } = new();
        public ImmutableArray<WardrobeSlot> Slots { get; init; } = ImmutableArray<WardrobeSlot>.Empty;
        public Exception? Failure { get; init; }
        public int LoadCount { get; private set; }
        public Task<ImmutableArray<WardrobeSlot>> LoadSlots(int userId)
        {
            LoadCount++;

            return Task.FromResult(Slots);
        }
        public void SaveSlot(int userId, int slotId, string look, string gender)
        {
            if (Failure != null) {
                throw Failure;
            }

            Saves.Add(new object[] { userId, slotId, look, gender });
        }
    }
}

public sealed class WardrobeDatabaseFactAttribute : Xunit.FactAttribute
{
    public WardrobeDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_WARDROBE_TEST_CONNECTION_STRING"))) {
            Skip = "Set PLUS_WARDROBE_TEST_CONNECTION_STRING to a server that can create and drop disposable task_wardrobe_tests_ schemas.";
        }
    }
}
