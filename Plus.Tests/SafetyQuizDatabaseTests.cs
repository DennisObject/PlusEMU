using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Plus.Database;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Help;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Talents;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("HousekeepingDatabase")]
public sealed class SafetyQuizDatabaseTests
{
    [SafetyQuizDatabaseFact]
    public void NativeQuestionPoolsAndRewardGroupsAreCompleteAndMigrationPreservesOperatorEditsOnRerun()
    {
        using var fixture = new Fixture();
        var safety = fixture.Store.Load("SafetyQuiz1")!;
        var way = fixture.Store.Load("HabboWay1")!;
        Assert.True(safety.Valid);
        Assert.True(way.Valid);
        Assert.Equal((13, 5), (safety.QuestionCount, way.QuestionCount));
        Assert.Equal(new[] { 1, 1, 1, 1, 1, 0, 1, 1, 2, 2, 0, 0, 0 }, safety.Questions.Select(question => question.CorrectAnswer));
        Assert.All(safety.Questions, question => Assert.Equal(3, question.AnswerCount));
        Assert.Equal(new[] { 2, 1, 2, 0, 1, 3, 1, 3, 0, 0 }, way.Questions.Select(question => question.CorrectAnswer));
        Assert.All(way.Questions, question => Assert.Equal(4, question.AnswerCount));
        Assert.Equal((5, 5, 1), fixture.Connection.QuerySingle<(int, int, int)>("SELECT reward_pixels,reward_points,progress_needed FROM achievements WHERE group_name='ACH_SafetyQuizGraduate'"));
        Assert.Equal((0, 0, 1), fixture.Connection.QuerySingle<(int, int, int)>("SELECT reward_pixels,reward_points,progress_needed FROM achievements WHERE group_name='ACH_HabboWayGraduate'"));
        fixture.Connection.Execute("UPDATE safety_quizzes SET enabled=FALSE,question_count=4 WHERE code='SafetyQuiz1'; UPDATE safety_quiz_questions SET correct_answer=2 WHERE quiz_code='SafetyQuiz1' AND question_id=0; UPDATE achievements SET reward_pixels=31 WHERE group_name='ACH_SafetyQuizGraduate'");
        fixture.Migrate();
        fixture.Migrate();
        Assert.Equal((false, 4), fixture.Connection.QuerySingle<(bool, int)>("SELECT enabled,question_count FROM safety_quizzes WHERE code='SafetyQuiz1'"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT correct_answer FROM safety_quiz_questions WHERE quiz_code='SafetyQuiz1' AND question_id=0"));
        Assert.Equal(31, fixture.Connection.ExecuteScalar<int>("SELECT reward_pixels FROM achievements WHERE group_name='ACH_SafetyQuizGraduate'"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM achievements"));
        Assert.Equal(23, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM safety_quiz_questions"));
    }

    [SafetyQuizDatabaseFact]
    public async Task RetryBoundariesAtomicCompletionAndConcurrentReplayPreserveOneUtcGraduation()
    {
        using var fixture = new Fixture();
        var now = new DateTimeOffset(2040, 2, 3, 4, 5, 6, TimeSpan.FromHours(5.5)).AddTicks(1234560);
        var failed = fixture.Store.Finish(7, "SafetyQuiz1", false, 7200, now)!;
        Assert.Null(failed.CompletedAt);
        Assert.Null(fixture.Store.Finish(7, "SafetyQuiz1", true, 7200, now.AddHours(2).AddTicks(-10)));
        var completed = fixture.Store.Finish(7, "SafetyQuiz1", true, 7200, now.AddHours(2))!;
        Assert.Equal(now.AddHours(2), completed.CompletedAt);
        Assert.True(completed.AwardPending);
        var replay = await Task.WhenAll(
            Task.Run(() => fixture.Store.Finish(7, "SafetyQuiz1", true, 7200, now.AddHours(8))),
            Task.Run(() => fixture.Store.Finish(7, "SafetyQuiz1", true, 7200, now.AddHours(9))));
        Assert.All(replay, state => Assert.Equal(completed.CompletedAt, state!.CompletedAt));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_safety_quizzes WHERE user_id=7"));
        fixture.Store.Awarded(7, "SafetyQuiz1");
        Assert.False(fixture.Store.Read(7, "SafetyQuiz1")!.AwardPending);
        Assert.Equal(completed.CompletedAt, fixture.Store.Read(7, "SafetyQuiz1")!.CompletedAt);
    }

    [SafetyQuizDatabaseFact]
    public void FailedCompletionUpdateRollsBackTheInitialLedgerRow()
    {
        using var fixture = new Fixture();
        fixture.Connection.Execute("CREATE TRIGGER fail_quiz_completion BEFORE UPDATE ON user_safety_quizzes FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced completion failure'");
        Assert.Throws<MySqlException>(() => fixture.Store.Finish(7, "SafetyQuiz1", true, 7200, DateTimeOffset.UtcNow));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_safety_quizzes"));
        fixture.Connection.Execute("DROP TRIGGER fail_quiz_completion");
        Assert.True(fixture.Store.Finish(7, "SafetyQuiz1", true, 7200, DateTimeOffset.UtcNow)!.AwardPending);
    }

    [SafetyQuizDatabaseFact]
    public async Task RealGraduationAndTalentPersistenceAreOnceOnlyAndReplayAfterAchievementSqlFailure()
    {
        using var fixture = new Fixture();
        var (service, habbo, client, packets) = await fixture.Runtime();
        service.Start(client, "SafetyQuiz1");
        var answers = fixture.Answers(packets, "SafetyQuiz1");
        fixture.Connection.Execute("DROP TABLE user_achievements");
        service.Submit(client, "SafetyQuiz1", answers);
        Assert.True(fixture.Store.Read(7, "SafetyQuiz1")!.AwardPending);
        Assert.Equal(0, habbo.GetAchievementData("ACH_SafetyQuizGraduate")!.Level);
        Assert.DoesNotContain(packets, packet => packet.Header == ServerPacketHeader.QuizResultsComposer);
        fixture.Connection.Execute(fixture.CreateTable("user_achievements"));
        var replay = await fixture.Runtime();
        replay.Service.Start(replay.Client, "SafetyQuiz1");
        Assert.False(fixture.Store.Read(7, "SafetyQuiz1")!.AwardPending);
        Assert.Equal(1, replay.Habbo.GetAchievementData("ACH_SafetyQuizGraduate")!.Level);
        Assert.Equal((5, 5), (replay.Habbo.Duckets, replay.Habbo.HabboStats.AchievementPoints));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_achievements WHERE userid=7 AND `group`='ACH_SafetyQuizGraduate' AND level=1"));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_talent_rewards WHERE user_id=7 AND level=0"));
        Assert.Contains(replay.Packets, packet => packet.Header == ServerPacketHeader.TalentLevelUpComposer);
        replay.Service.Start(replay.Client, "HabboWay1");
        replay.Service.Submit(replay.Client, "HabboWay1", fixture.Answers(replay.Packets, "HabboWay1"));
        Assert.False(fixture.Store.Read(7, "HabboWay1")!.AwardPending);
        Assert.Equal(1, replay.Habbo.GetAchievementData("ACH_HabboWayGraduate")!.Level);
        Assert.Equal((5, 5), (replay.Habbo.Duckets, replay.Habbo.HabboStats.AchievementPoints));
        replay.Service.Start(replay.Client, "HabboWay1");
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_achievements WHERE userid=7 AND level=1"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_badges WHERE user_id=7"));
        Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_talent_rewards WHERE user_id=7"));
    }

    [SafetyQuizDatabaseFact]
    public async Task DisconnectBetweenCompletionAndManagerEntryLeavesGraduationPendingForTheNextLoadedSession()
    {
        using var fixture = new Fixture();
        var current = await fixture.Runtime(habbo => habbo.OnDisconnect());
        current.Service.Start(current.Client, "SafetyQuiz1");
        current.Service.Submit(current.Client, "SafetyQuiz1", fixture.Answers(current.Packets, "SafetyQuiz1"));
        Assert.True(current.Habbo.WalletClosed);
        Assert.Null(current.Habbo.Client);
        Assert.True(fixture.Store.Read(7, "SafetyQuiz1")!.AwardPending);
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_achievements"));
        Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_badges"));
        Assert.DoesNotContain(current.Packets, packet => packet.Header == ServerPacketHeader.QuizResultsComposer);

        var replay = await fixture.Runtime();
        replay.Service.Start(replay.Client, "SafetyQuiz1");
        Assert.False(fixture.Store.Read(7, "SafetyQuiz1")!.AwardPending);
        Assert.Equal(1, replay.Habbo.GetAchievementData("ACH_SafetyQuizGraduate")!.Level);
        replay.Habbo.Save();
        Assert.Equal((5, 5), fixture.Connection.QuerySingle<(int, int)>("SELECT u.activity_points,s.AchievementScore FROM users u JOIN user_statistics s ON s.id=u.id WHERE u.id=7"));

        // The production login task loads the committed level, so replay cannot repeat its economic reward.
        fixture.Connection.Execute("UPDATE user_safety_quizzes SET award_pending=TRUE WHERE user_id=7");
        var loaded = await fixture.Runtime();
        Assert.Equal(1, loaded.Habbo.GetAchievementData("ACH_SafetyQuizGraduate")!.Level);
        loaded.Service.Start(loaded.Client, "SafetyQuiz1");
        Assert.Equal((5, 5), (loaded.Habbo.Duckets, loaded.Habbo.HabboStats.AchievementPoints));
        Assert.False(fixture.Store.Read(7, "SafetyQuiz1")!.AwardPending);
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_achievements"));
        Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_talent_rewards"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MySqlConnection _admin;
        private readonly FieldInfo _game = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object? _previousGame;
        private readonly string _schema = "task_safety_quiz_" + Guid.NewGuid().ToString("N");
        private readonly string _dump = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
        public MySqlConnection Connection { get; }
        public HabbiconDatabaseTests.TestDatabase Database { get; }
        public SafetyQuizStore Store { get; }
        public Fixture()
        {
            _previousGame = _game.GetValue(null);
            SqlMapper.AddTypeHandler(new UtcDateTimeOffsetHandler());
            var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SAFETY_QUIZ_DATABASE")!)
            {
                Pooling = false,
                AllowZeroDateTime = true,
                ConvertZeroDateTime = true,
                AllowUserVariables = true
            };
            _admin = new(options.ConnectionString);
            _admin.Open();
            _admin.Execute($"CREATE DATABASE `{_schema}`");
            options.Database = _schema;
            Connection = new(options.ConnectionString);
            Database = new(options.ConnectionString);
            Store = new(Database);

            try {
                Connection.Open();
                foreach (var table in new[] { "users", "users_settings", "user_stats", "achievements", "badge_definitions", "user_badges", "user_achievements", "talents", "talents_sub_levels" }) {
                    Connection.Execute(CreateTable(table));
                }
                Connection.Execute("ALTER TABLE users ADD bubble_id TINYINT NOT NULL DEFAULT 0; ALTER TABLE user_stats RENAME TO user_statistics");
                Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/47_TalentTrackRewards.sql")));
                Connection.Execute("INSERT INTO users(id,username,auth_ticket,activity_points) VALUES(7,'quiz','quiz-ticket',0); INSERT INTO users_settings(user_id) VALUES(7); INSERT INTO user_statistics(id) VALUES(7); INSERT INTO talents(type,level,data_actions,data_gifts) VALUES('citizenship',0,'TRADE',''),('citizenship',1,'TRADE',''); INSERT INTO talents_sub_levels(talent_type,talent_level,sub_level,badge_code,required_progress) VALUES('citizenship',0,1,'ACH_SafetyQuizGraduate1',1),('citizenship',1,1,'ACH_HabboWayGraduate1',1)");
                Migrate();
                var clients = CatalogSnapshotTestSupport.Proxy<IGameClientManager>((method, _) =>
                    method == "UnregisterClient" ? null : throw new InvalidOperationException(method));
                _game.SetValue(null, CatalogSnapshotTestSupport.Proxy<IGame>((method, _) =>
                    method == "get_ClientManager" ? clients : throw new InvalidOperationException(method)));
            }
            catch {
                Dispose();
                throw;
            }
        }
        public string CreateTable(string table)
        {
            var ddl = Regex.Match(_dump, $@"CREATE TABLE `{table}` \([\s\S]*?\) ENGINE=[^;]+;").Value;
            Assert.NotEmpty(ddl);
            return ddl;
        }
        public void Migrate() => Connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/56_SafetyQuizzes.sql")));
        public int[] Answers(List<(uint Header, byte[] Payload)> packets, string code)
        {
            var packet = new FlashIncomingPacket { Buffer = packets.Last(packet => packet.Header == ServerPacketHeader.QuizDataComposer).Payload };
            Assert.Equal(code, packet.ReadString());
            var definition = Store.Load(code)!;
            return Enumerable.Range(0, packet.ReadInt()).Select(_ => packet.ReadInt()).Select(id => definition.Questions.Single(question => question.Id == id).CorrectAnswer).ToArray();
        }
        public async Task<(SafetyQuizService Service, Habbo Habbo, GameClient Client, List<(uint Header, byte[] Payload)> Packets)> Runtime(Action<Habbo>? beforeAchievement = null)
        {
            var habbo = new Habbo
            {
                Id = 7,
                Username = "quiz",
                Access = UserAccess.Empty,
                SessionStartedAt = DateTimeOffset.UtcNow,
                Persistence = new UserPersistenceService(Database, TimeProvider.System),
                Duckets = Connection.ExecuteScalar<int>("SELECT activity_points FROM users WHERE id=7"),
                HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "old", 0),
                Inventory = new InventoryComponent { Furniture = new FurnitureInventoryComponent([], []), Badges = new BadgesInventoryComponent([]) }
            };
            habbo.HabboStats.AchievementPoints = Connection.ExecuteScalar<int>("SELECT AchievementScore FROM user_statistics WHERE id=7");
            await new LoadUserAchievementsTask(Database).Load(habbo);
            var (client, packets) = HabbiconTestSupport.Client(habbo);
            habbo.Client = client;
            var badges = new BadgeManager(Database, TestGameClientManager.Empty, TestLogging.For<BadgeManager>());
            await badges.Init();
            foreach (var badge in await badges.LoadBadgesForHabbo(7)) {
                habbo.Inventory.Badges.AddBadge(badge);
            }
            var definitions = new TalentTrackManager(NullLogger<TalentTrackManager>.Instance, Database);
            await definitions.Start();
            var items = CatalogSnapshotTestSupport.Proxy<IItemDataManager>((_, _) => throw new InvalidOperationException("Empty configured gifts do not need item metadata."));
            var talents = new TalentTrackProgressionService(definitions, items, new TalentTrackRewardStore(Database), NullLogger<TalentTrackProgressionService>.Instance);
            var manager = new AchievementManager(new AchievementLevelFactory(Database), Database, badges, talents);
            await manager.Init();
            var entry = CatalogSnapshotTestSupport.Proxy<IAchievementManager>((method, args) =>
            {
                if (method == "get_Achievements") {
                    return manager.Achievements;
                }
                if (method == "ProgressAchievement") {
                    beforeAchievement?.Invoke(habbo);
                    return manager.ProgressAchievement((GameClient)args![0]!, (string)args[1]!, (int)args[2]!, (bool)args[3]!);
                }
                throw new InvalidOperationException(method);
            });
            return (new(Store, entry, talents, new AccountSessionGate(), TimeProvider.System, NullLogger<SafetyQuizService>.Instance), habbo, client, packets);
        }
        public void Dispose()
        {
            _game.SetValue(null, _previousGame);
            Connection.Dispose();
            try {
                _admin.Execute($"DROP DATABASE IF EXISTS `{_schema}`");
            }
            finally {
                _admin.Dispose();
            }
        }
    }
}

public sealed class SafetyQuizDatabaseFactAttribute : FactAttribute
{
    public SafetyQuizDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SAFETY_QUIZ_DATABASE"))) {
            Skip = "Set SAFETY_QUIZ_DATABASE for isolated native-schema quiz regressions.";
        }
    }
}
