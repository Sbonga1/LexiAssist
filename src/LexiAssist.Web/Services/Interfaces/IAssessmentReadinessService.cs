using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Services.Interfaces;

public interface IAssessmentReadinessService
{
    Task<AssessmentReadinessResult?> GetReadinessAsync(int screeningTestId, CancellationToken cancellationToken = default);
}

public enum AssessmentReadinessSeverity
{
    Passed,
    Warning,
    Blocking
}

public sealed record AssessmentReadinessCheck(
    string Key,
    string Label,
    AssessmentReadinessSeverity Severity,
    string? Detail = null,
    string? FixUrl = null,
    string? FixLabel = null);

public sealed record AssessmentReadinessQuestion(
    int Id,
    int SortOrder,
    string Prompt,
    string QuestionTypeName,
    bool IsActive,
    AssessmentReadinessSeverity Severity,
    IReadOnlyList<string> Issues,
    string EditUrl);

public sealed record AssessmentQuestionTypeCount(string Name, int Count);

public sealed class AssessmentReadinessResult
{
    public int ScreeningTestId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Instructions { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int TotalQuestionCount { get; init; }
    public int ActiveQuestionCount { get; init; }
    public int InactiveQuestionCount => TotalQuestionCount - ActiveQuestionCount;
    public IReadOnlyList<AssessmentReadinessQuestion> Questions { get; init; } = [];
    public IReadOnlyList<AssessmentReadinessCheck> Checks { get; init; } = [];
    public IReadOnlyList<AssessmentQuestionTypeCount> QuestionTypeCounts { get; init; } = [];
    public bool IsReady => Checks.All(check => check.Severity != AssessmentReadinessSeverity.Blocking);
    public IReadOnlyList<AssessmentReadinessCheck> BlockingIssues => Checks.Where(check => check.Severity == AssessmentReadinessSeverity.Blocking).ToArray();
    public IReadOnlyList<AssessmentReadinessCheck> Warnings => Checks.Where(check => check.Severity == AssessmentReadinessSeverity.Warning).ToArray();

    public string ActivationFeedback => ActiveQuestionCount == 0
        ? "Add and activate at least one question before publishing this assessment."
        : "Resolve the blocking readiness issues before publishing: " +
          string.Join(" ", BlockingIssues.Take(3).Select(issue => issue.Detail ?? issue.Label));
}
