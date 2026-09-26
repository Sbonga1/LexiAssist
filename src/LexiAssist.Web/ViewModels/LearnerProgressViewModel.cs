namespace LexiAssist.Web.ViewModels;

public sealed record LearnerProgressViewModel(
    IReadOnlyList<ScreeningProgressPointViewModel> ScreeningHistory,
    IReadOnlyList<ScreeningCategoryScoreViewModel> ScreeningCategoryScores,
    IReadOnlyList<PracticeActivitySummaryViewModel> PracticeActivities,
    IReadOnlyList<ProgressAccuracyPointViewModel> AccuracyTrend,
    IReadOnlyList<PracticeFrequencyPointViewModel> ActivityFrequency,
    IReadOnlyList<TargetSkillAreaProgressViewModel> TargetSkillAreas)
{
    public bool HasAnyData => ScreeningHistory.Count > 0 ||
                               PracticeActivities.Count > 0 ||
                               AccuracyTrend.Count > 0 ||
                               ActivityFrequency.Count > 0 ||
                               TargetSkillAreas.Count > 0;
}

public sealed record ScreeningProgressPointViewModel(
    int AttemptId,
    DateTime CompletedAtUtc,
    double? OverallScore,
    int CategoryCount);

public sealed record ScreeningCategoryScoreViewModel(
    string CategoryName,
    DateTime CompletedAtUtc,
    double? PercentageScore);

public sealed record PracticeActivitySummaryViewModel(
    string ActivityName,
    int AttemptCount,
    int CompletedAttemptCount,
    double? AverageAccuracy,
    DateTime? LastAttemptAtUtc);

public sealed record ProgressAccuracyPointViewModel(
    DateTime DateUtc,
    double AverageAccuracy,
    int AttemptCount);

public sealed record PracticeFrequencyPointViewModel(
    DateTime DateUtc,
    int ActivityCount);

public sealed record TargetSkillAreaProgressViewModel(
    string Name,
    double? LatestPercentage,
    double? PreviousPercentage,
    double? ChangePercentagePoints,
    int ObservationCount,
    bool IsFlaggedForSupport);
