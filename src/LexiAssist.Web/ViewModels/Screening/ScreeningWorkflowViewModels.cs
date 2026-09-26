namespace LexiAssist.Web.ViewModels.Screening;

using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.ViewModels.Recordings;

public sealed record ScreeningCategoryViewModel(int Id, string Name, string Description);

public sealed record ScreeningLandingViewModel(
    bool HasIncompleteAttempt,
    DateTime? AttemptStartedAtUtc,
    bool HasAvailableQuestions,
    IReadOnlyList<ScreeningCategoryViewModel> Categories);

public sealed record ScreeningStartViewModel(
    bool CanStart,
    IReadOnlyList<ScreeningCategoryViewModel> Categories);

public sealed record ScreeningSkillAreaResultViewModel(
    string Name,
    double? PercentageScore,
    bool IsStrength,
    bool IsFlaggedForSupport,
    string Summary)
{
    public IReadOnlyList<ScreeningActivityMetric> ActivityMetrics { get; init; } = [];
}

public sealed record ScreeningResultViewModel(
    DateTime GeneratedAtUtc,
    ScreeningSupportTier? SupportTier,
    string TierDescription,
    string Explanation,
    string RecommendedNextAction,
    string? ScoringVersion,
    bool IsReferralRecommended,
    IReadOnlyList<ScreeningSkillAreaResultViewModel> SkillAreas,
    IReadOnlyList<ScreeningSkillAreaResultViewModel> Strengths,
    IReadOnlyList<ScreeningSkillAreaResultViewModel> SupportAreas)
{
    public ScreeningSkillAreaResultViewModel? MemoryAndBroaderThinkingSkills { get; init; }
}

public sealed class ScreeningRunnerViewModel
{
    public DateTime AttemptStartedAtUtc { get; init; }

    public bool IsUnavailable { get; init; }

    public bool IsTimedNaming { get; init; }

    public bool IsReadingFluency { get; init; }

    public bool IsReadingComprehension { get; init; }

    public bool IsListeningComprehension { get; init; }

    public bool IsWorkingMemorySequence { get; init; }

    public bool IsTimedMatching { get; init; }

    public bool IsPatternReasoning { get; init; }

    public string? ScreeningTestName { get; init; }

    public string? Prompt { get; init; }

    public string? TestInstructions { get; init; }

    public ScreeningQuestionType? QuestionType { get; init; }

    public string? QuestionTypeName { get; init; }

    public IReadOnlyList<string> AnswerChoices { get; init; } = Array.Empty<string>();

    public string? PassageText { get; init; }

    public string? AudioSourceUrl { get; init; }

    public IReadOnlyList<string> WorkingMemorySequence { get; init; } = Array.Empty<string>();

    public int MemoryDisplayDurationMilliseconds { get; init; }

    public int TimedMatchingTimeLimitSeconds { get; init; }

    public IReadOnlyDictionary<string, string> TimedMatchingReferenceMapping { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<string> TimedMatchingItems { get; init; } = Array.Empty<string>();

    public string? ImagePath { get; init; }

    public int? PatternDifficultyLevel { get; init; }

    public int? MaxAudioReplays { get; init; }

    public string? UnavailableMessage { get; init; }

    public int TimedNamingItemCount { get; init; }

    public int TimedNamingTimeLimitSeconds { get; init; }

    public string? QuestionToken { get; init; }

    public string? TextAnswer { get; init; }

    public string? SelectedAnswer { get; init; }

    public int QuestionNumber { get; init; }

    public int TotalQuestionCount { get; init; }

    public int ProgressPercentage { get; init; }

    public LearnerAudioRecorderViewModel? AudioRecorder { get; init; }
}

public sealed class ScreeningAnswerInputViewModel
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string QuestionToken { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.StringLength(8000)]
    public string? TextAnswer { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(2000)]
    public string? SelectedAnswer { get; set; }

    public bool TimedOut { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(8000)]
    public string? TimedMatchingAnswersJson { get; set; }
}

public sealed class TimedNamingStartInputViewModel
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string QuestionToken { get; set; } = string.Empty;
}

public sealed class TimedNamingFinishInputViewModel
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string RunToken { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string CompletedOrderJson { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string SelfReportedErrorIndexesJson { get; set; } = string.Empty;
}

public sealed class ReadingFluencyStartInputViewModel
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string QuestionToken { get; set; } = string.Empty;
}

public sealed class ReadingFluencyFinishInputViewModel
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(4096)]
    public string RunToken { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Range(0, 5000)]
    [System.ComponentModel.DataAnnotations.Display(Name = "Correction count")]
    public int? CorrectionCount { get; set; }
}
