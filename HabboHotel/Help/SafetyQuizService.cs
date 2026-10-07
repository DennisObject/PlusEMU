using System.Collections.Immutable;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Help;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Talents;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Help;

[Singleton]
public interface ISafetyQuizService
{
    void Start(GameClient session, string code);
    void Submit(GameClient session, string code, IReadOnlyList<int> answers);
}

public sealed class SafetyQuizService(ISafetyQuizStore store, IAchievementManager achievements,
    ITalentTrackProgressionService talents, IAccountSessionGate sessions, TimeProvider clock,
    ILogger<SafetyQuizService> logger) : ISafetyQuizService
{
    private readonly ConditionalWeakTable<Habbo, Pending> _pending = new();

    public void Start(GameClient session, string code)
    {
        var habbo = session.GetHabbo();

        if (habbo == null || !Active(session, habbo) || code is not ("SafetyQuiz1" or "HabboWay1")) {
            return;
        }

        using var gate = sessions.Enter(habbo.Id);

        try {
            var definition = store.Load(code);

            if (definition is not { Valid: true } || !achievements.Achievements.TryGetValue(definition.Achievement!, out var achievement)
                || !achievement.Levels.TryGetValue(1, out var graduation) || graduation.Requirement <= 0) {
                return;
            }

            SafetyQuizProgress? progress;

            lock (habbo.WalletSync) {
                if (!Active(session, habbo)) {
                    return;
                }

                progress = store.Read(habbo.Id, code);
            }

            if (progress?.CompletedAt != null && !Reconcile(session, habbo, definition, progress)) {
                return;
            }

            ImmutableArray<int> ids;

            lock (habbo.WalletSync) {
                if (!Active(session, habbo)) {
                    return;
                }

                var holder = _pending.GetValue(habbo, _ => new());
                holder.Attempt = null;

                if (progress?.NextAllowedAt > clock.GetUtcNow()) {
                    ids = [];
                }
                else {
                    // Only question ids are sent. Texts and shuffled option labels belong to the client.
                    var pool = definition.Questions.ToArray();
                    Random.Shared.Shuffle(pool);
                    var questions = pool.Take(definition.QuestionCount).ToImmutableArray();
                    holder.Attempt = new(session, definition, questions);
                    ids = questions.Select(question => question.Id).ToImmutableArray();
                }
            }

            session.Send(new QuizDataComposer(code, ids));
        }
        catch (Exception exception) when (exception is DbException or AggregateException) {
            logger.LogError(exception, "Could not start safety quiz {QuizCode} for user {UserId}", code, habbo.Id);
        }
    }

    public void Submit(GameClient session, string code, IReadOnlyList<int> answers)
    {
        var habbo = session.GetHabbo();

        if (habbo == null || code is not ("SafetyQuiz1" or "HabboWay1") || answers.Count is < 1 or > 64) {
            return;
        }

        using var gate = sessions.Enter(habbo.Id);

        try {
            Attempt attempt;
            ImmutableArray<int> wrong;
            SafetyQuizProgress? progress;

            lock (habbo.WalletSync) {
                if (!Active(session, habbo) || !_pending.TryGetValue(habbo, out var holder)
                    || holder.Attempt is not { } current || !ReferenceEquals(current.Session, session)
                    || current.Definition.Code != code || answers.Count != current.Questions.Length) {
                    return;
                }

                attempt = current;
                wrong = current.Questions.Where((question, index) => answers[index] != question.CorrectAnswer)
                    .Select(question => question.Id).ToImmutableArray();
                progress = store.Finish(habbo.Id, code, wrong.IsEmpty, current.Definition.RetrySeconds, clock.GetUtcNow());

                if (progress == null) {
                    return;
                }
            }

            // BadgeManager's asynchronous wait stays outside WalletSync. A failed award remains durable and replayable.
            if (wrong.IsEmpty && !Reconcile(session, habbo, attempt.Definition, progress)) {
                return;
            }

            lock (habbo.WalletSync) {
                if (!Active(session, habbo)) {
                    return;
                }

                _pending.GetValue(habbo, _ => new()).Attempt = null;
            }

            session.Send(new QuizResultsComposer(code, wrong));
        }
        catch (Exception exception) when (exception is DbException or AggregateException) {
            logger.LogError(exception, "Could not finish safety quiz {QuizCode} for user {UserId}", code, habbo.Id);
        }
    }

    private bool Reconcile(GameClient session, Habbo habbo, SafetyQuizDefinition definition, SafetyQuizProgress progress)
    {
        if (!Active(session, habbo) || progress.CompletedAt == null
            || !achievements.Achievements.TryGetValue(definition.Achievement!, out var achievement)
            || !achievement.Levels.TryGetValue(1, out var graduation)) {
            return false;
        }

        if ((habbo.GetAchievementData(definition.Achievement!)?.Level ?? 0) < 1) {
            achievements.ProgressAchievement(session, definition.Achievement!, graduation.Requirement, fromBeginning: true);
        }

        if (!Active(session, habbo) || (habbo.GetAchievementData(definition.Achievement!)?.Level ?? 0) < 1) {
            return false;
        }

        // A crash after achievement commit may precede its talent hook or this acknowledgement.
        talents.Progress(habbo, achievements.Achievements);

        if (progress.AwardPending) {
            store.Awarded(habbo.Id, definition.Code);
        }

        return Active(session, habbo);
    }

    private static bool Active(GameClient session, Habbo habbo) => !habbo.AccessClosed
        && ReferenceEquals(session.GetHabbo(), habbo) && ReferenceEquals(habbo.Client, session);

    private sealed class Pending
    {
        public Attempt? Attempt { get; set; }
    }
    private sealed record Attempt(GameClient Session, SafetyQuizDefinition Definition, ImmutableArray<SafetyQuizQuestion> Questions);
}
