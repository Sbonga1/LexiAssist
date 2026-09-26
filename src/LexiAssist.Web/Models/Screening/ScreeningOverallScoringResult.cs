namespace LexiAssist.Web.Models.Screening;

public sealed record ScreeningOverallScoringResult(
    decimal RawScore,
    decimal AvailableScore,
    decimal? Percentage,
    IReadOnlyList<string> FlaggedSkillAreas,
    ScreeningCompletionStatus CompletionStatus,
    int AnsweredQuestionCount,
    int ExpectedQuestionCount,
    IReadOnlyList<ScreeningTestScoringResult> TestResults);
