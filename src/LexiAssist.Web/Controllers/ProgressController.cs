using System.Security.Claims;
using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Learner)]
[Route("progress")]
public sealed class ProgressController(ApplicationDbContext dbContext) : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [HttpGet("")]
    [HttpGet("/Dashboard/Progress")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var learnerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (learnerId is null)
        {
            return Unauthorized();
        }

        var completedScreenings = await dbContext.ScreeningAttempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == learnerId &&
                              attempt.Status == ScreeningAttemptStatus.Completed)
            .OrderBy(attempt => attempt.CompletedAtUtc ?? attempt.StartedAtUtc)
            .Select(attempt => new ScreeningRow(
                attempt.Id,
                attempt.CompletedAtUtc ?? attempt.StartedAtUtc,
                attempt.OverallScore))
            .ToListAsync(cancellationToken);

        var screeningCategoryRows = await dbContext.ScreeningTestAttempts
            .AsNoTracking()
            .Where(testAttempt => testAttempt.ScreeningAttempt.UserId == learnerId &&
                                  testAttempt.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed)
            .OrderBy(testAttempt => testAttempt.CompletedAtUtc ?? testAttempt.StartedAtUtc)
            .Select(testAttempt => new ScreeningCategoryRow(
                testAttempt.ScreeningAttemptId,
                testAttempt.ScreeningTest.Name,
                testAttempt.CompletedAtUtc ?? testAttempt.StartedAtUtc,
                testAttempt.PercentageScore))
            .ToListAsync(cancellationToken);

        var screeningCategoryCounts = screeningCategoryRows
            .GroupBy(row => row.AttemptId)
            .ToDictionary(group => group.Key, group => group.Count());

        var screeningHistory = completedScreenings
            .Select(screening => new ScreeningProgressPointViewModel(
                screening.Id,
                screening.CompletedAtUtc,
                screening.OverallScore,
                screeningCategoryCounts.GetValueOrDefault(screening.Id)))
            .ToArray();

        var screeningCategoryScores = screeningCategoryRows
            .Select(row => new ScreeningCategoryScoreViewModel(
                row.CategoryName,
                row.CompletedAtUtc,
                row.PercentageScore))
            .ToArray();

        var activityRows = await dbContext.ActivityAttempts
            .AsNoTracking()
            .Where(attempt => attempt.ActivityAssignment.LearnerId == learnerId &&
                              attempt.ActivityAssignment.Status != ActivityAssignmentStatus.Cancelled)
            .Select(attempt => new ActivityRow(
                attempt.ActivityAssignment.LearningActivity.Name,
                attempt.StartedAtUtc,
                attempt.CompletedAtUtc,
                attempt.Accuracy))
            .ToListAsync(cancellationToken);

        var practiceActivities = activityRows
            .GroupBy(row => row.ActivityName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var accuracyValues = group
                    .Where(row => row.Accuracy is not null)
                    .Select(row => row.Accuracy!.Value)
                    .ToArray();

                return new PracticeActivitySummaryViewModel(
                    group.Key,
                    group.Count(),
                    group.Count(row => row.CompletedAtUtc is not null),
                    accuracyValues.Length == 0 ? null : Math.Round(accuracyValues.Average(), 2),
                    group.Max(row => (DateTime?)row.StartedAtUtc));
            })
            .ToArray();

        var accuracyTrend = activityRows
            .Where(row => row.Accuracy is not null)
            .GroupBy(row => row.StartedAtUtc.Date)
            .OrderBy(group => group.Key)
            .Select(group => new ProgressAccuracyPointViewModel(
                group.Key,
                Math.Round(group.Average(row => row.Accuracy!.Value), 2),
                group.Count()))
            .ToArray();

        var activityFrequency = activityRows
            .GroupBy(row => row.StartedAtUtc.Date)
            .OrderBy(group => group.Key)
            .Select(group => new PracticeFrequencyPointViewModel(group.Key, group.Count()))
            .ToArray();

        var reports = await dbContext.ScreeningReports
            .AsNoTracking()
            .Where(report => report.UserId == learnerId &&
                             report.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed)
            .OrderBy(report => report.GeneratedAtUtc)
            .Select(report => new ReportRow(report.GeneratedAtUtc, report.SkillAreasJson))
            .ToListAsync(cancellationToken);

        var targetSkillAreas = BuildTargetSkillAreas(reports);

        return View(new LearnerProgressViewModel(
            screeningHistory,
            screeningCategoryScores,
            practiceActivities,
            accuracyTrend,
            activityFrequency,
            targetSkillAreas));
    }

    private static IReadOnlyList<TargetSkillAreaProgressViewModel> BuildTargetSkillAreas(
        IReadOnlyList<ReportRow> reports)
    {
        var observations = reports
            .SelectMany(report => ParseSkillAreas(report.SkillAreasJson)
                .Where(area => !string.IsNullOrWhiteSpace(area.Name))
                .Select(area => new SkillAreaObservation(
                    area.Name.Trim(),
                    report.GeneratedAtUtc,
                    area.PercentageScore,
                    area.IsFlaggedForSupport)))
            .GroupBy(area => area.Name, StringComparer.OrdinalIgnoreCase);

        return observations
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ordered = group.OrderBy(area => area.DateUtc).ToArray();
                var latest = ordered[^1];
                var previous = ordered.Length > 1 ? ordered[^2] : null;
                var change = latest.PercentageScore is { } latestScore &&
                             previous?.PercentageScore is { } previousScore
                    ? Math.Round((double)(latestScore - previousScore), 2)
                    : (double?)null;

                return new TargetSkillAreaProgressViewModel(
                    latest.Name,
                    latest.PercentageScore is { } latestPercentage ? (double)latestPercentage : null,
                    previous?.PercentageScore is { } previousPercentage ? (double)previousPercentage : null,
                    change,
                    ordered.Length,
                    latest.IsFlaggedForSupport);
            })
            .ToArray();
    }

    private static IReadOnlyList<ScreeningReportSkillAreaSnapshot> ParseSkillAreas(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ScreeningReportSkillAreaSnapshot>();
        }

        try
        {
            return JsonSerializer.Deserialize<ScreeningReportSkillAreaSnapshot[]>(json, JsonOptions)
                   ?? Array.Empty<ScreeningReportSkillAreaSnapshot>();
        }
        catch (JsonException)
        {
            return Array.Empty<ScreeningReportSkillAreaSnapshot>();
        }
    }

    private sealed record ScreeningRow(int Id, DateTime CompletedAtUtc, double? OverallScore);

    private sealed record ScreeningCategoryRow(
        int AttemptId,
        string CategoryName,
        DateTime CompletedAtUtc,
        double? PercentageScore);

    private sealed record ActivityRow(
        string ActivityName,
        DateTime StartedAtUtc,
        DateTime? CompletedAtUtc,
        double? Accuracy);

    private sealed record ReportRow(DateTime GeneratedAtUtc, string SkillAreasJson);

    private sealed record SkillAreaObservation(
        string Name,
        DateTime DateUtc,
        decimal? PercentageScore,
        bool IsFlaggedForSupport);
}
