namespace LexiAssist.Web.Models.Screening;

/// <summary>
/// Descriptive, non-clinical measures for an original screening activity.
/// Null values mean that the stored data was not sufficient to calculate that measure.
/// </summary>
public sealed record ScreeningActivityMetric(
    ScreeningQuestionType ActivityType,
    string ActivityName,
    int? ItemsAttempted,
    int? ItemsCorrect,
    int? ItemsIncorrect,
    int? AvailableItemCount,
    decimal? AccuracyPercentage,
    int? HighestCorrectlyCompletedSequenceLength,
    long? ElapsedTimeMilliseconds,
    double? AverageResponseTimeMilliseconds,
    double? MedianResponseTimeMilliseconds,
    decimal? RawCorrectCount,
    decimal? AvailableScore,
    decimal? Percentage);
