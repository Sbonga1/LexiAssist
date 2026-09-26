namespace LexiAssist.Web.Models.Screening;

public sealed record ScreeningQuestionScore(
    int QuestionId,
    bool HasResponse,
    bool IsScorable,
    bool? IsCorrect,
    decimal RawScore,
    decimal AvailableScore);
