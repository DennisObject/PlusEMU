using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms.Polls;

[Singleton]
public interface IRoomPollStore
{
    RoomPollSnapshot? Load(uint roomId);
    bool Completed(int pollId, int userId);
    bool Answer(RoomPollSnapshot poll, int userId, int questionId, string[] answers, DateTimeOffset now);
}

public sealed class RoomPollStore(IDatabase database) : IRoomPollStore
{
    public RoomPollSnapshot? Load(uint roomId)
    {
        using var connection = database.Connection();
        var poll = connection.QuerySingleOrDefault<PollRow>(
            "SELECT id, room_id RoomId, type, title, summary, end_message EndMessage, nps FROM room_polls WHERE room_id = @roomId AND enabled = TRUE", new { roomId });

        if (poll == null) {
            return null;
        }

        var rows = connection.Query<QuestionRow>(
            "SELECT id, parent_id ParentId, sort_order SortOrder, type, text, category, answer_type AnswerType, choices FROM room_poll_questions WHERE poll_id = @id ORDER BY sort_order, id", new { poll.Id }).ToArray();

        if (rows.Length == 0 || rows.Length > 128 || rows.Any(row => row.Id <= 0 || row.Type is < 1 or > 4 ||
                row.ParentId != 0 && !rows.Any(parent => parent.Id == row.ParentId && parent.ParentId == 0))) {
            throw new DataException("Invalid room poll question configuration.");
        }

        PollQuestionSnapshot Snapshot(QuestionRow row) => new(row.Id, row.SortOrder, row.Type, row.Text,
            row.Category, row.AnswerType, JsonSerializer.Deserialize<ImmutableArray<PollChoice>>(row.Choices),
            rows.Where(child => child.ParentId == row.Id).Select(Snapshot).ToImmutableArray());
        var questions = rows.Where(row => row.ParentId == 0).Select(Snapshot).ToImmutableArray();

        if (questions.SelectMany(question => new[] { question }.Concat(question.Children)).Any(question =>
                question.Choices.IsDefault || question.Choices.Length > 64 ||
                question.Choices.Any(choice => choice == null || choice.Value == null || choice.Label == null ||
                    !int.TryParse(choice.Value, out var value) || value.ToString(System.Globalization.CultureInfo.InvariantCulture) != choice.Value) ||
                question.Choices.Select(choice => choice.Value).Distinct(StringComparer.Ordinal).Count() != question.Choices.Length ||
                question.Type is 1 or 2 && question.Choices.Length == 0 ||
                question.Type is 3 or 4 && question.Choices.Length != 0 ||
                question.Children.Select(child => child.Category).Distinct().Count() != question.Children.Length ||
                question.Children.Length > 0 && (!poll.Nps || question.Type != 1 ||
                    question.Children.Any(child => child.Category <= 0 || !question.Choices.Any(choice => choice.Category == child.Category))))) {
            throw new DataException("Invalid room poll choices.");
        }

        return new(poll.Id, roomId, poll.Type, poll.Title, poll.Summary, poll.EndMessage, poll.Nps, questions);
    }

    public bool Completed(int pollId, int userId)
    {
        using var connection = database.Connection();

        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_poll_responses WHERE poll_id = @pollId AND user_id = @userId AND completed_at IS NOT NULL", new { pollId, userId }) != 0;
    }

    public bool Answer(RoomPollSnapshot poll, int userId, int questionId, string[] answers, DateTimeOffset now)
    {
        var question = poll.Find(questionId);

        if (question == null || !RoomPollSnapshot.ValidAnswer(question, answers)) {
            return false;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("INSERT IGNORE INTO room_poll_responses (poll_id, user_id, answers) VALUES (@pollId, @userId, '{}')", new { pollId = poll.Id, userId }, transaction);
        var response = connection.QuerySingle<ResponseRow>("SELECT answers, (completed_at IS NOT NULL) Completed FROM room_poll_responses WHERE poll_id = @pollId AND user_id = @userId FOR UPDATE", new { pollId = poll.Id, userId }, transaction);

        if (response.Completed) {
            return false;
        }

        var recorded = JsonSerializer.Deserialize<Dictionary<int, string[]>>(response.Answers)!;

        if (!poll.IsAvailable(question, recorded)) {
            return false;
        }

        recorded[questionId] = answers;
        var complete = poll.IsComplete(recorded);
        connection.Execute("UPDATE room_poll_responses SET answers = @answers, completed_at = @completedAt WHERE poll_id = @pollId AND user_id = @userId",
            new { pollId = poll.Id, userId, answers = JsonSerializer.Serialize(recorded), completedAt = complete ? (DateTime?)now.UtcDateTime : null }, transaction);
        transaction.Commit();

        return true;
    }

    private sealed class PollRow
    {
        public int Id { get; set; }
        public uint RoomId { get; set; }
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public string EndMessage { get; set; } = "";
        public bool Nps { get; set; }
    }

    private sealed class QuestionRow
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public int SortOrder { get; set; }
        public int Type { get; set; }
        public string Text { get; set; } = "";
        public int Category { get; set; }
        public int AnswerType { get; set; }
        public string Choices { get; set; } = "[]";
    }

    private sealed class ResponseRow
    {
        public string Answers { get; set; } = "{}";
        public bool Completed { get; set; }
    }
}
