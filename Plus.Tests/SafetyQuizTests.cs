using System.Collections.Immutable;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Help;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Help;
using Plus.HabboHotel.Talents;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

public sealed class SafetyQuizTests
{
    [Theory]
    [InlineData("SafetyQuiz1")]
    [InlineData("HabboWay1")]
    public void AnswersAreGradedAgainstTheActualServedSnapshotBeforeGraduationAndResults(string code)
    {
        var fixture = new Fixture(code);
        fixture.Service.Start(fixture.Client, code);
        var ids = fixture.QuestionIds();
        Assert.Equal(3, ids.Length);
        Assert.Equal(3, ids.Distinct().Count());
        fixture.Store.Definition = fixture.Store.Definition with
        {
            Questions = fixture.Store.Definition.Questions.Select(question => question with { CorrectAnswer = 0 }).ToImmutableArray()
        };
        fixture.Store.BeforeFinish = () =>
        {
            Assert.Equal(0, fixture.Manager.Calls);
            Assert.Equal(0, fixture.TalentCalls);
            Assert.Single(fixture.Packets);
        };
        fixture.Service.Submit(fixture.Client, code, ids.Select(id => id % 3).ToArray());
        Assert.Equal(1, fixture.Manager.Calls);
        Assert.Equal(1, fixture.TalentCalls);
        Assert.True(fixture.Store.State!.CompletedAt.HasValue);
        Assert.False(fixture.Store.State.AwardPending);
        Assert.Equal(1, fixture.Habbo.GetAchievementData(fixture.Group)!.Level);
        var result = new FlashIncomingPacket { Buffer = fixture.Packets.Last().Payload };
        Assert.Equal(code, result.ReadString());
        Assert.Equal(0, result.ReadInt());
        Assert.False(result.HasDataRemaining());
        fixture.Service.Submit(fixture.Client, code, ids.Select(id => id % 3).ToArray());
        Assert.Equal(1, fixture.Manager.Calls);
        Assert.Equal(1, fixture.Store.Finishes);
    }

    [Fact]
    public void WrongAnswersReportTheQuestionIdsAndPersistRetryTimingWithoutAnyAward()
    {
        var fixture = new Fixture("SafetyQuiz1");
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        var ids = fixture.QuestionIds();
        var answers = ids.Select(id => id % 3).ToArray();
        answers[0] = -1;
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", answers);
        var result = new FlashIncomingPacket { Buffer = fixture.Packets.Last().Payload };
        Assert.Equal("SafetyQuiz1", result.ReadString());
        Assert.Equal(1, result.ReadInt());
        Assert.Equal(ids[0], result.ReadInt());
        Assert.Equal(0, fixture.Manager.Calls);
        Assert.Null(fixture.Store.State!.CompletedAt);
        Assert.Equal(fixture.Clock.Now.AddHours(2), fixture.Store.State.NextAllowedAt);
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        Assert.Empty(fixture.QuestionIds());
        fixture.Clock.Now = fixture.Store.State.NextAllowedAt;
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        Assert.Equal(3, fixture.QuestionIds().Length);
    }

    [Fact]
    public void UnservedWrongCodeShortExtraAndReplacementSessionSubmissionsCannotClaimSuccess()
    {
        var fixture = new Fixture("SafetyQuiz1");
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", [0, 1, 2]);
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        var answers = fixture.QuestionIds().Select(id => id % 3).ToArray();
        fixture.Service.Submit(fixture.Client, "HabboWay1", answers);
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", answers[..2]);
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", [.. answers, 1]);
        var (replacement, _) = HabbiconTestSupport.Client(fixture.Habbo);
        fixture.Habbo.Client = replacement;
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", answers);
        fixture.Service.Submit(replacement, "SafetyQuiz1", answers);
        Assert.Equal(0, fixture.Store.Finishes);
        Assert.Equal(0, fixture.Manager.Calls);
    }

    [Fact]
    public void CompletionBeforeAFailedGraduationIsReplayedOnTheNextLoadedSession()
    {
        var fixture = new Fixture("SafetyQuiz1");
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        fixture.Manager.Fail = true;
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", fixture.QuestionIds().Select(id => id % 3).ToArray());
        Assert.True(fixture.Store.State!.AwardPending);
        Assert.NotNull(fixture.Store.State.CompletedAt);
        Assert.Single(fixture.Packets);
        fixture.Manager.Fail = false;
        var loaded = new Habbo { Id = 7 };
        var (client, _) = HabbiconTestSupport.Client(loaded);
        loaded.Client = client;
        fixture.Service.Start(client, "SafetyQuiz1");
        Assert.Equal(2, fixture.Manager.Calls);
        Assert.Equal(1, loaded.GetAchievementData(fixture.Group)!.Level);
        Assert.False(fixture.Store.State.AwardPending);
    }

    [Fact]
    public void ACommittedGraduationWithFailedAcknowledgementReconcilesWithoutRepeatingTheReward()
    {
        var fixture = new Fixture("HabboWay1");
        fixture.Service.Start(fixture.Client, "HabboWay1");
        fixture.Store.FailAcknowledgement = true;
        fixture.Service.Submit(fixture.Client, "HabboWay1", fixture.QuestionIds().Select(id => id % 3).ToArray());
        Assert.True(fixture.Store.State!.AwardPending);
        Assert.Equal(1, fixture.Manager.Calls);
        fixture.Store.FailAcknowledgement = false;
        fixture.Service.Start(fixture.Client, "HabboWay1");
        Assert.Equal(1, fixture.Manager.Calls);
        Assert.False(fixture.Store.State.AwardPending);
        Assert.Equal(2, fixture.TalentCalls);
    }

    [Fact]
    public void AFailedCompletionWriteCannotPublishOrAwardAndTheServedAttemptCanRetry()
    {
        var fixture = new Fixture("SafetyQuiz1");
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        var answers = fixture.QuestionIds().Select(id => id % 3).ToArray();
        fixture.Store.FailFinish = true;
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", answers);
        Assert.Null(fixture.Store.State);
        Assert.Equal(0, fixture.Manager.Calls);
        Assert.Single(fixture.Packets);
        fixture.Store.FailFinish = false;
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", answers);
        Assert.False(fixture.Store.State!.AwardPending);
    }

    [Fact]
    public void DisabledIncompleteAndUnknownConfigurationCannotInventAQuestionSetOrAward()
    {
        var fixture = new Fixture("SafetyQuiz1");
        fixture.Service.Start(fixture.Client, "unknown");
        fixture.Store.Definition = fixture.Store.Definition with { Enabled = false };
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        fixture.Store.Definition = fixture.Store.Definition with { Enabled = true, Questions = [] };
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        Assert.Empty(fixture.Packets);
        Assert.Equal(0, fixture.Store.Finishes);
        Assert.Equal(0, fixture.Manager.Calls);
    }

    [Theory]
    [InlineData("SafetyQuiz1")]
    [InlineData("HabboWay1")]
    public async Task PublicQuizPacketHandlersServeAndGradeTheNativeAnswerArray(string code)
    {
        var fixture = new Fixture(code);
        await new GetQuizQuestionsEvent(fixture.Service).Parse(fixture.Client, HabbiconTestSupport.Incoming(code));
        var answers = fixture.QuestionIds().Select(id => id % 3).ToArray();
        await new PostQuizAnswersEvent(fixture.Service).Parse(fixture.Client,
            HabbiconTestSupport.Incoming(new object[] { code, answers.Length }.Concat(answers.Cast<object>()).ToArray()));
        Assert.False(fixture.Store.State!.AwardPending);
        Assert.Equal(1, fixture.Manager.Calls);
        Assert.Equal(ServerPacketHeader.QuizResultsComposer, fixture.Packets.Last().Header);
    }

    [Fact]
    public void DisposedHabboStillAttachedToItsClientCannotReadFinishOrAwardAQuiz()
    {
        var fixture = new Fixture("SafetyQuiz1");
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        var answers = fixture.QuestionIds().Select(id => id % 3).ToArray();
        fixture.Habbo.Dispose();
        Assert.False(fixture.Habbo.WalletClosed);
        Assert.True(fixture.Habbo.AccessClosed);
        Assert.Same(fixture.Client, fixture.Habbo.Client);
        fixture.Service.Start(fixture.Client, "SafetyQuiz1");
        fixture.Service.Submit(fixture.Client, "SafetyQuiz1", answers);
        Assert.Equal(1, fixture.Store.Loads);
        Assert.Equal(0, fixture.Store.Finishes);
        Assert.Equal(0, fixture.Manager.Calls);
        Assert.Equal(0, fixture.TalentCalls);
        Assert.Single(fixture.Packets);

        var manager = new AchievementManager(null!, null!, null!, null!);
        manager.Achievements.Add(fixture.Group, fixture.Manager.Achievements[fixture.Group]);
        Assert.False(manager.ProgressAchievement(fixture.Client, fixture.Group, 1, true));
    }

    [Fact]
    public void GenuineAchievementEntryRejectsMissingHabboBeforeAnyBadgeOrDatabaseAccess()
    {
        var manager = new AchievementManager(null!, null!, null!, null!);
        manager.Achievements.Add("ACH_SafetyQuizGraduate", new() { GroupName = "ACH_SafetyQuizGraduate" });
        var (client, _) = HabbiconTestSupport.Client(null!);
        Assert.False(manager.ProgressAchievement(client, "ACH_SafetyQuizGraduate", 1, true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65)]
    [InlineData(int.MaxValue)]
    public async Task IncomingAnswerBoundsRejectMalformedOrOversizedCountsBeforeAllocating(int count)
    {
        var service = CatalogSnapshotTestSupport.Proxy<ISafetyQuizService>((_, _) => throw new InvalidOperationException("No quiz access expected."));
        var packet = HabbiconTestSupport.Incoming("SafetyQuiz1", count);
        await new PostQuizAnswersEvent(service).Parse(null!, packet);
    }

    [Fact]
    public async Task IncomingPacketsRejectTruncatedCodesAnswersAndTrailingBytes()
    {
        var service = CatalogSnapshotTestSupport.Proxy<ISafetyQuizService>((_, _) => throw new InvalidOperationException("No quiz access expected."));
        await new GetQuizQuestionsEvent(service).Parse(null!, new FlashIncomingPacket { Buffer = new byte[] { 0, 32 } });
        await new GetQuizQuestionsEvent(service).Parse(null!, HabbiconTestSupport.Incoming("SafetyQuiz1", 1));
        await new PostQuizAnswersEvent(service).Parse(null!, HabbiconTestSupport.Incoming("SafetyQuiz1", 3, 0));
        await new PostQuizAnswersEvent(service).Parse(null!, HabbiconTestSupport.Incoming("SafetyQuiz1", 1, 0, 0));
    }

    private sealed class Fixture
    {
        public Habbo Habbo { get; } = new() { Id = 7 };
        public GameClient Client { get; }
        public List<(uint Header, byte[] Payload)> Packets { get; }
        public RecordingStore Store { get; }
        public RecordingAchievements Manager { get; }
        public Clock Clock { get; } = new();
        public SafetyQuizService Service { get; }
        public int TalentCalls { get; private set; }
        public string Group { get; }
        public Fixture(string code)
        {
            (Client, Packets) = HabbiconTestSupport.Client(Habbo);
            Habbo.Client = Client;
            Store = new(new(code, true, 3, 7200, Enumerable.Range(0, 3).Select(id => new SafetyQuizQuestion(id, 3, id % 3)).ToImmutableArray()));
            Group = Store.Definition.Achievement!;
            Manager = new(Group);
            var talents = CatalogSnapshotTestSupport.Proxy<ITalentTrackProgressionService>((method, _) =>
            {
                Assert.Equal("Progress", method);
                TalentCalls++;
                return null;
            });
            Service = new(Store, Manager, talents, new AccountSessionGate(), Clock, NullLogger<SafetyQuizService>.Instance);
        }
        public int[] QuestionIds()
        {
            var packet = new FlashIncomingPacket { Buffer = Packets.Last(packet => packet.Header == ServerPacketHeader.QuizDataComposer).Payload };
            Assert.Equal(Store.Definition.Code, packet.ReadString());
            return Enumerable.Range(0, packet.ReadInt()).Select(_ => packet.ReadInt()).ToArray();
        }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2040, 2, 3, 4, 5, 6, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class RecordingStore(SafetyQuizDefinition definition) : ISafetyQuizStore
    {
        public SafetyQuizDefinition Definition { get; set; } = definition;
        public SafetyQuizProgress? State { get; private set; }
        public int Loads { get; private set; }
        public int Finishes { get; private set; }
        public Action? BeforeFinish { get; set; }
        public bool FailFinish { get; set; }
        public bool FailAcknowledgement { get; set; }
        public SafetyQuizDefinition? Load(string code)
        {
            Loads++;
            return code == Definition.Code ? Definition : null;
        }
        public SafetyQuizProgress? Read(int userId, string code) => State;
        public SafetyQuizProgress? Finish(int userId, string code, bool passed, int retrySeconds, DateTimeOffset now)
        {
            BeforeFinish?.Invoke();
            if (FailFinish) {
                throw new DatabaseFailure();
            }
            Finishes++;
            return State ??= new(now.AddSeconds(retrySeconds), passed ? now : null, passed);
        }
        public void Awarded(int userId, string code)
        {
            if (FailAcknowledgement) {
                throw new DatabaseFailure();
            }
            State = State! with { AwardPending = false };
        }
    }
    private sealed class RecordingAchievements(string group) : IAchievementManager
    {
        public Dictionary<string, Achievement> Achievements { get; } = new()
        {
            [group] = new() { GroupName = group, Levels = new() { [1] = new(1, 5, 5, 1) } }
        };
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public bool ProgressAchievement(GameClient session, string name, int progress, bool fromBeginning = false)
        {
            Assert.False(Monitor.IsEntered(session.GetHabbo().WalletSync));
            Assert.Equal((group, 1, true), (name, progress, fromBeginning));
            Calls++;
            if (Fail) {
                throw new DatabaseFailure();
            }
            session.GetHabbo().Achievements[name] = new(name, 1, 0);
            return true;
        }
        public Task Init() => Task.CompletedTask;
        public ICollection<Achievement> GetGameAchievements(int gameId) => [];
    }
    private sealed class DatabaseFailure() : DbException("forced quiz database failure");
}
