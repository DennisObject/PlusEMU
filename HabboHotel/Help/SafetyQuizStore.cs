using System.Collections.Immutable;
using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Help
{
    public sealed record SafetyQuizProgress(DateTimeOffset NextAllowedAt, DateTimeOffset? CompletedAt, bool AwardPending);

    [Singleton]
    public interface ISafetyQuizStore
    {
        SafetyQuizDefinition? Load(string code);
        SafetyQuizProgress? Read(int userId, string code);
        SafetyQuizProgress? Finish(int userId, string code, bool passed, int retrySeconds, DateTimeOffset now);
        void Awarded(int userId, string code);
    }

    public sealed class SafetyQuizStore(IDatabase database) : ISafetyQuizStore
    {
        public SafetyQuizDefinition? Load(string code)
        {
            using var connection = database.Connection();
            var row = connection.QuerySingleOrDefault<DefinitionRow>(
                "SELECT enabled,question_count AS QuestionCount,retry_seconds AS RetrySeconds FROM safety_quizzes WHERE BINARY code=BINARY @code", new { code });

            if (row == null) {
                return null;
            }

            var questions = connection.Query<QuestionRow>(
                "SELECT question_id AS Id,answer_count AS AnswerCount,correct_answer AS CorrectAnswer FROM safety_quiz_questions WHERE BINARY quiz_code=BINARY @code ORDER BY question_id", new { code });

            return new(code, row.Enabled, row.QuestionCount, row.RetrySeconds,
                questions.Select(question => new SafetyQuizQuestion(question.Id, question.AnswerCount, question.CorrectAnswer)).ToImmutableArray());
        }

        public SafetyQuizProgress? Read(int userId, string code)
        {
            using var connection = database.Connection();
            var row = connection.QuerySingleOrDefault<ProgressRow>(
                "SELECT next_allowed_at AS NextAllowedAt,completed_at AS CompletedAt,award_pending AS AwardPending FROM user_safety_quizzes WHERE user_id=@userId AND BINARY quiz_code=BINARY @code", new { userId, code });

            return row == null ? null : new(row.NextAllowedAt, row.CompletedAt, row.AwardPending);
        }

        public SafetyQuizProgress? Finish(int userId, string code, bool passed, int retrySeconds, DateTimeOffset now)
        {
            if (userId <= 0 || code is not ("SafetyQuiz1" or "HabboWay1") || retrySeconds is < 0 or > 86400) {
                return null;
            }

            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id=@userId FOR UPDATE", new { userId }, transaction) == null) {
                return null;
            }

            connection.Execute("INSERT IGNORE INTO user_safety_quizzes(user_id,quiz_code,next_allowed_at) VALUES(@userId,@code,@now)",
                new { userId, code, now = now.UtcDateTime }, transaction);
            var current = connection.QuerySingle<ProgressRow>(
                "SELECT next_allowed_at AS NextAllowedAt,completed_at AS CompletedAt,award_pending AS AwardPending FROM user_safety_quizzes WHERE user_id=@userId AND BINARY quiz_code=BINARY @code FOR UPDATE",
                new { userId, code }, transaction);

            if (current.CompletedAt == null && current.NextAllowedAt > now) {
                return null;
            }

            // Replaying a committed perfect attempt keeps its original completion and pending award.
            if (current.CompletedAt != null) {
                return new(current.NextAllowedAt, current.CompletedAt, current.AwardPending);
            }

            var completed = passed ? now : (DateTimeOffset?)null;
            var next = now.AddSeconds(retrySeconds);
            connection.Execute("UPDATE user_safety_quizzes SET next_allowed_at=@next,completed_at=@completed,award_pending=@passed WHERE user_id=@userId AND BINARY quiz_code=BINARY @code",
                new { userId, code, next = next.UtcDateTime, completed = completed?.UtcDateTime, passed }, transaction);
            transaction.Commit();

            return new(next, completed, passed);
        }

        public void Awarded(int userId, string code)
        {
            using var connection = database.Connection();
            connection.Execute("UPDATE user_safety_quizzes SET award_pending=FALSE WHERE user_id=@userId AND BINARY quiz_code=BINARY @code AND completed_at IS NOT NULL", new { userId, code });
        }

        private sealed class DefinitionRow
        {
            public bool Enabled { get; set; }
            public int QuestionCount { get; set; }
            public int RetrySeconds { get; set; }
        }
        private sealed class QuestionRow
        {
            public int Id { get; set; }
            public int AnswerCount { get; set; }
            public int CorrectAnswer { get; set; }
        }
        private sealed class ProgressRow
        {
            public DateTimeOffset NextAllowedAt { get; set; }
            public DateTimeOffset? CompletedAt { get; set; }
            public bool AwardPending { get; set; }
        }
    }
}
