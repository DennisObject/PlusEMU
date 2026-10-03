using System.Data;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Core.Settings;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class CameraDatabaseFactAttribute : FactAttribute
{
    public CameraDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_CAMERA_TEST_CONNECTION_STRING")))
            Skip = "Set PLUS_CAMERA_TEST_CONNECTION_STRING to a disposable task_camera_tests_ schema.";
    }
}

public class CameraCheckoutTests
{
    private const int UserId = 910001;
    private readonly TestDatabase _database;
    private readonly TestSettings _settings = new();
    private readonly TestDefinitions _definitions = new();
    private readonly Clock _clock = new();
    private readonly CameraCheckoutService _service;
    private readonly Habbo _habbo;
    private readonly CameraCheckoutMedia _media;

    public CameraCheckoutTests()
    {
        var value = Environment.GetEnvironmentVariable("PLUS_CAMERA_TEST_CONNECTION_STRING")!;
        if (!new MySqlConnectionStringBuilder(value).Database.StartsWith("task_camera_tests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Camera database tests require a disposable schema.");
        _database = new(value);
        _service = new(_database, _settings, _definitions, _clock);
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        _habbo = new() { Id = UserId, Username = "Camera transaction", Credits = 100, Duckets = 20, Diamonds = 3, CurrentRoom = room };
        _media = new(Guid.NewGuid(), 42, _clock.GetUtcNow());
        Execute("DROP TRIGGER IF EXISTS camera_test_failure; DELETE FROM camera_quota; DELETE FROM camera_purchases; DELETE FROM camera_publications; DELETE FROM camera_accounts; DELETE FROM camera_competition_entries; DELETE FROM camera_media; DELETE FROM items; DELETE FROM users");
        Execute("INSERT INTO users (id,username,credits,activity_points,vip_points) VALUES (910001,'camera_tests',100,20,3)");
        Mint(_media);
    }

    [CameraDatabaseFact]
    public void CaptureAndEditQuotasAreIndependentAndSurviveRecreation()
    {
        var quota = new CameraQuota(_database, _settings, _clock);
        Assert.True(quota.Reserve(UserId, false, false));
        Assert.False(quota.Reserve(UserId, false, false));
        Assert.True(quota.Reserve(UserId, true, false));
        Assert.False(quota.Reserve(UserId, true, false));
        _clock.Now = _clock.Now.AddSeconds(1);
        Assert.True(new CameraQuota(_database, _settings, _clock).Reserve(UserId, true, false));
        Assert.False(quota.Reserve(UserId, false, false));
        _clock.Now = _clock.Now.AddSeconds(4);
        Assert.True(quota.Reserve(UserId, false, true));
        _clock.Now = _clock.Now.AddSeconds(5);
        Assert.False(quota.Reserve(UserId, false, true));
        Assert.True(quota.Reserve(UserId, false, false));
    }

    [CameraDatabaseFact]
    public void QuotaCooldownCrossesMidnightAndDailyCountsReset()
    {
        _clock.Now = new DateTimeOffset(2026, 10, 2, 23, 59, 59, TimeSpan.Zero);
        _settings.Values["camera.render.daily"] = "1";
        var quota = new CameraQuota(_database, _settings, _clock);
        Assert.True(quota.Reserve(UserId, false, false));
        _clock.Now = _clock.Now.AddSeconds(2);
        Assert.False(quota.Reserve(UserId, false, false));
        _clock.Now = _clock.Now.AddSeconds(3);
        Assert.True(quota.Reserve(UserId, false, false));
        _clock.Now = _clock.Now.AddSeconds(5);
        Assert.False(quota.Reserve(UserId, false, false));
    }

    [CameraDatabaseFact]
    public void RealPurchasePersistsTrustedMetadataAndChargesForEachCopy()
    {
        var first = _service.Purchase(_habbo, _media);
        var second = _service.Purchase(_habbo, _media);
        Assert.True(first.Ok); Assert.True(second.Ok);
        Assert.NotEqual(first.Item!.Id, second.Item!.Id);
        Assert.Equal((96, 20, 3), (_habbo.Credits, _habbo.Duckets, _habbo.Diamonds));
        Assert.Equal(96, Scalar("SELECT credits FROM users WHERE id=910001"));
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM camera_purchases"));
        using var connection = _database.Connection();
        var metadata = connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=@id", new { id = first.Item.Id });
        Assert.Contains($"/camera/{_media.Id:D}.png", metadata);
        Assert.DoesNotContain("http", metadata);
    }

    [CameraDatabaseFact]
    public void ForgedOwnerRoomDefinitionAndInsufficientBalanceCreateNothing()
    {
        Assert.False(_service.Purchase(_habbo, _media with { Id = Guid.NewGuid() }).Ok);
        Assert.False(_service.Purchase(_habbo, _media with { RoomId = 43 }).Ok);
        Execute("UPDATE camera_media SET user_id=999");
        Assert.False(_service.Purchase(_habbo, _media).Ok);
        Execute("UPDATE camera_media SET user_id=910001");
        _settings.Values["camera.item_id"] = "0";
        Assert.False(_service.Purchase(_habbo, _media).Ok);
        _settings.Values["camera.item_id"] = "123";
        _habbo.Credits = 1;
        Assert.False(_service.Purchase(_habbo, _media).Ok);
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(100, Scalar("SELECT credits FROM users"));
    }

    [CameraDatabaseFact]
    public void FailureAfterItemInsertionRollsBackItemOwnershipAndWallet()
    {
        Execute("CREATE TRIGGER camera_test_failure BEFORE UPDATE ON users FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced rollback'");
        try { Assert.Throws<MySqlException>(() => _service.Purchase(_habbo, _media)); }
        finally { Execute("DROP TRIGGER camera_test_failure"); }
        Assert.Equal(100, _habbo.Credits);
        Assert.Equal(100, Scalar("SELECT credits FROM users"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM items"));
        Assert.Equal(0, Scalar("SELECT COUNT(*) FROM camera_purchases"));
    }

    [CameraDatabaseFact]
    public void PublishIsIdempotentAndCooldownSurvivesServiceRecreation()
    {
        Assert.True(_service.Publish(_habbo, _media).Changed);
        Assert.True(_service.Publish(_habbo, _media).Ok);
        Assert.Equal(19, _habbo.Duckets);
        var next = _media with { Id = Guid.NewGuid() }; Mint(next);
        var recreated = new CameraCheckoutService(_database, _settings, _definitions, _clock);
        var blocked = recreated.Publish(_habbo, next);
        Assert.False(blocked.Ok); Assert.Equal(180, blocked.WaitSeconds);
        Assert.Equal(19, Scalar("SELECT activity_points FROM users"));
        _clock.Now = _clock.Now.AddSeconds(180);
        Assert.True(recreated.Publish(_habbo, next).Ok);
        Assert.Equal(18, _habbo.Duckets);
    }

    [CameraDatabaseFact]
    public void CompetitionIsDisabledByDefaultIdempotentAndDailyBounded()
    {
        Assert.False(_service.EnterCompetition(_habbo, _media).Ok);
        _settings.Values["camera.competition.enabled"] = "1";
        _settings.Values["camera.competition.daily"] = "1";
        Assert.True(_service.EnterCompetition(_habbo, _media).Changed);
        Assert.True(_service.EnterCompetition(_habbo, _media).Ok);
        var next = _media with { Id = Guid.NewGuid() }; Mint(next);
        Assert.Equal("limit", _service.EnterCompetition(_habbo, next).Error);
        _settings.Values["camera.competition.require_email"] = "1";
        Assert.Equal("email", _service.EnterCompetition(_habbo, next).Error);
        Assert.Equal(1, Scalar("SELECT COUNT(*) FROM camera_competition_entries"));
        Assert.Equal(100, _habbo.Credits); Assert.Equal(20, _habbo.Duckets);
    }

    [CameraDatabaseFact]
    public void ScheduledCleanupDrainsBacklogWithoutCapturesAndPreservesRetainedMedia()
    {
        Assert.True(_service.Purchase(_habbo, _media).Ok);
        _clock.Now = _clock.Now.AddMinutes(31);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(directory);
        try
        {
            for (var index = 0; index < 105; index++)
            {
                var expired = _media with { Id = Guid.NewGuid() };
                Mint(expired);
                File.WriteAllText(Path.Combine(directory, expired.Id + ".png"), "expired");
                File.WriteAllText(Path.Combine(directory, expired.Id + "_small.png"), "expired");
            }
            var fresh = _media with { Id = Guid.NewGuid(), CreatedAt = _clock.GetUtcNow() }; Mint(fresh);
            using var cleanup = new CameraMediaCleanup(_database,
                Microsoft.Extensions.Options.Options.Create(new CameraConfiguration { OutputDirectory = directory }),
                _clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<CameraMediaCleanup>.Instance);
            cleanup.Start().GetAwaiter().GetResult();
            Assert.True(SpinWait.SpinUntil(() => Scalar("SELECT COUNT(*) FROM camera_media") == 2, TimeSpan.FromSeconds(10)));
            Assert.Empty(Directory.GetFiles(directory));
            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM camera_purchases"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [CameraDatabaseFact]
    public void CleanupRetriesPartialUnlinksAndRetainsDatabaseRecordOnFailure()
    {
        _clock.Now = _clock.Now.AddMinutes(31);
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(directory);
        string main = Path.Combine(directory, _media.Id + ".png");
        string small = Path.Combine(directory, _media.Id + "_small.png");
        File.WriteAllText(main, "expired");
        Directory.CreateDirectory(small); // File.Delete must fail after the main file was removed.
        try
        {
            using var cleanup = new CameraMediaCleanup(_database,
                Microsoft.Extensions.Options.Options.Create(new CameraConfiguration { OutputDirectory = directory }),
                _clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<CameraMediaCleanup>.Instance);
            Assert.Equal(1, cleanup.SweepBatch().Count);
            Assert.False(File.Exists(main));
            Assert.Equal(1, Scalar("SELECT COUNT(*) FROM camera_media"));
            Directory.Delete(small);
            Assert.Equal(1, cleanup.SweepBatch().Count);
            Assert.Equal(0, Scalar("SELECT COUNT(*) FROM camera_media"));
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    private void Mint(CameraCheckoutMedia media)
    {
        using var c = _database.Connection();
        c.Execute("INSERT INTO camera_media (id,user_id,room_id,created_at) VALUES (@id,910001,@roomId,@now)",
            new { id = media.Id.ToString("D"), roomId = media.RoomId, now = media.CreatedAt.UtcDateTime });
    }
    private void Execute(string sql) { using var c = _database.Connection(); c.Execute(sql); }
    private int Scalar(string sql) { using var c = _database.Connection(); return c.QuerySingle<int>(sql); }
    private sealed class TestDatabase(string value) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(value);
        public bool IsConnected() => true;
        public IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
    }
    private sealed class TestSettings : ISettingsManager
    {
        public Dictionary<string,string> Values = new()
        {
            ["camera.render.daily"]="50", ["camera.render.cooldown"]="5", ["camera.render.edit.daily"]="200", ["camera.render.edit.cooldown"]="1", ["camera.thumbnail.cooldown"]="15",
            ["camera.enabled"]="1", ["camera.item_id"]="123", ["camera.price.credits"]="2", ["camera.price.points"]="0",
            ["camera.price.points.type"]="0", ["camera.price.publish.points"]="1", ["camera.price.publish.points.type"]="0",
            ["camera.publish.cooldown"]="180", ["camera.competition.enabled"]="0", ["camera.competition.daily"]="3", ["camera.competition.require_email"]="0"
        };
        public string TryGetValue(string key) => Values.GetValueOrDefault(key,"0");
        public string? GetOptionalValue(string key) => Values.GetValueOrDefault(key);
        public Task Reload() => Task.CompletedTask;
    }
    private sealed class TestDefinitions : IItemDataManager
    {
        public Dictionary<uint,ItemDefinition> Items { get; } = new() { [123] = new() { Id=123, SpriteId=4597, Type=ItemType.Wall, InteractionType=InteractionType.CameraPicture } };
        public Dictionary<int,uint> Gifts { get; } = new();
        public ItemDefinition GetItemByName(string name) => Items[123];
        public void Init() { }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026,10,2,18,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
