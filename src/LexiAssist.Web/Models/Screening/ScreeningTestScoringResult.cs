namespace LexiAssist.Web.Models.Screening;

public sealed record ScreeningTestScoringResult(
    int ScreeningTestId,
    string TestName,
    string TestSlug,
    decimal RawScore,
    decimal AvailableScore,
    decimal? Percentage,
    IReadOnlyList<string> FlaggedSkillAreas,
    ScreeningCompletionStatus CompletionStatus,
    int AnsweredQuestionCount,
    int ExpectedQuestionCount,
    int ScorableQuestionCount,
    IReadOnlyList<ScreeningQuestionScore> QuestionScores)
{
    public IReadOnlyList<ScreeningActivityMetric> ActivityMetrics { get; init; } = [];
}
