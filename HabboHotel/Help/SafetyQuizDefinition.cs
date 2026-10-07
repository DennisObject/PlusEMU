using System.Collections.Immutable;

namespace Plus.HabboHotel.Help;

public sealed record SafetyQuizQuestion(int Id, int AnswerCount, int CorrectAnswer);
public sealed record SafetyQuizDefinition(string Code, bool Enabled, int QuestionCount, int RetrySeconds,
    ImmutableArray<SafetyQuizQuestion> Questions)
{
    public string? Achievement => Code switch
    {
        "SafetyQuiz1" => "ACH_SafetyQuizGraduate",
        "HabboWay1" => "ACH_HabboWayGraduate",
        _ => null
    };

    public bool Valid => Enabled && Achievement != null && QuestionCount is >= 1 and <= 64
        && RetrySeconds is >= 0 and <= 86400 && Questions.Length >= QuestionCount && Questions.Length <= 64
        && Questions.Select(question => question.Id).Distinct().Count() == Questions.Length
        && Questions.All(question => question.Id is >= 0 and < 64 && question.AnswerCount is >= 1 and <= 64
            && question.CorrectAnswer >= 0 && question.CorrectAnswer < question.AnswerCount);
}
