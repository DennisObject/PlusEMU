using System.Collections.Immutable;

namespace Plus.HabboHotel.Rooms.Polls;

public sealed record PollChoice(string Value, string Label, int Category);
public sealed record PollQuestionSnapshot(int Id, int Order, int Type, string Text, int Category,
    int AnswerType, ImmutableArray<PollChoice> Choices, ImmutableArray<PollQuestionSnapshot> Children);
public sealed record RoomPollSnapshot(int Id, uint RoomId, string Type, string Title, string Summary,
    string EndMessage, bool Nps, ImmutableArray<PollQuestionSnapshot> Questions)
{
    public PollQuestionSnapshot? Find(int id) => Questions.SelectMany(question =>
        new[] { question }.Concat(question.Children)).FirstOrDefault(question => question.Id == id);

    public bool IsAvailable(PollQuestionSnapshot question, IReadOnlyDictionary<int, string[]> answers)
    {
        var parent = Questions.FirstOrDefault(parent => parent.Children.Contains(question));
        return parent == null || Nps && answers.TryGetValue(parent.Id, out var values) &&
            parent.Choices.Any(choice => values.Contains(choice.Value) && choice.Category != 0 && choice.Category == question.Category);
    }

    public bool IsComplete(IReadOnlyDictionary<int, string[]> answers) => Questions.All(question =>
        answers.TryGetValue(question.Id, out var values) && ValidAnswer(question, values) &&
        question.Children.Where(child => IsAvailable(child, answers))
            .All(child => answers.TryGetValue(child.Id, out var childValues) && ValidAnswer(child, childValues)));

    public static bool ValidAnswer(PollQuestionSnapshot question, string[] answers) =>
        answers.Length <= 64 && answers.All(answer => answer.Length <= 4000) &&
        answers.Distinct(StringComparer.Ordinal).Count() == answers.Length &&
        (question.Type is 1 or 2
            ? (question.Type != 1 || answers.Length == 1) && answers.All(answer => question.Choices.Any(choice => choice.Value == answer))
            : question.Type is 3 or 4 && answers.Length == 1);
}
