using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.ViewModels.Referrals;

public sealed class PsychologistDataPassportViewModel
{
    public int ReferralId { get; init; }

    public int ScreeningAttemptId { get; init; }

    public string LearnerName { get; init; } = string.Empty;

    public string LearnerEmail { get; init; } = string.Empty;

    public DateOnly? DateOfBirth { get; init; }

    public DateTime ScreeningStartedAtUtc { get; init; }

    public DateTime ScreeningCompletedAtUtc { get; init; }

    public DateTime ReferralCreatedAtUtc { get; init; }

    public string? RiskTier { get; init; }

    public string ScreeningSummary { get; init; } = string.Empty;

    public string? ProfessionalNotes { get; init; }

    public IReadOnlyList<ScreeningReportSkillAreaSnapshot> CategoryScores { get; init; } = [];

    public ScreeningReportSkillAreaSnapshot? MemoryAndBroaderThinkingSkills { get; init; }

    public IReadOnlyList<ScreeningReportSkillAreaSnapshot> FlaggedSkillAreas { get; init; } = [];

    public IReadOnlyList<PassportTestTimingViewModel> TestTimings { get; init; } = [];

    public IReadOnlyList<PassportResponseCategoryViewModel> ResponseCategories { get; init; } = [];

    public IReadOnlyList<ScreeningErrorCategory> AvailableErrorCategories { get; init; } = [];

    public IReadOnlyList<PassportResponseTaggingViewModel> ResponsesForTagging { get; init; } = [];

    public IReadOnlyList<PassportRecordingViewModel> Recordings { get; init; } = [];

    public int UnavailableRecordingCount { get; init; }

    public IReadOnlyList<PassportPreviousScreeningViewModel> PreviousScreenings { get; init; } = [];

    public IReadOnlyList<PassportPreviousOptionalScreeningViewModel> PreviousMemoryAndBroaderThinkingSkills { get; init; } = [];
}

public sealed record PassportTestTimingViewModel(
    string CategoryName,
    long? DurationMilliseconds,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record PassportResponseCategoryViewModel(
    string Category,
    int ResponseCount,
    int CorrectCount,
    int ErrorCount,
    int NotObjectivelyScoredCount,
    double? AverageResponseTimeMilliseconds,
    string? AdditionalErrorLabel,
    int? AdditionalErrorCount);

public sealed record PassportResponseTaggingViewModel(
    int ResponseId,
    string QuestionType,
    string Prompt,
    string? SubmittedAnswer,
    IReadOnlyList<ScreeningErrorCategory> ErrorCategories);

public sealed record PassportRecordingViewModel(
    int ResponseId,
    string CategoryName,
    string ContentType,
    int? PrivateRecordingId = null,
    long? DurationMilliseconds = null,
    string? ItemPrompt = null,
    IReadOnlyList<ScreeningErrorCategory>? ErrorCategories = null,
    string? ProfessionalNote = null,
    DateTime? ProfessionalNoteUpdatedAtUtc = null);

public sealed class UpdateAudioSnippetNoteViewModel
{
    [System.ComponentModel.DataAnnotations.StringLength(500)]
    public string? ProfessionalNote { get; set; }
}

public sealed record PassportPreviousScreeningViewModel(
    DateTime ScreeningDateUtc,
    double? OverallPercentage,
    string? RiskTier);

public sealed record PassportPreviousOptionalScreeningViewModel(
    DateTime ScreeningDateUtc,
    IReadOnlyList<ScreeningActivityMetric> ActivityMetrics);
