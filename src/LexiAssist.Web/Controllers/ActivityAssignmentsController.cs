using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels.Activities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator + "," + ApplicationRoles.Psychologist)]
[Route("activity-assignments")]
public sealed class ActivityAssignmentsController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ILearningActivityService learningActivityService) : Controller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("create/{reportId:int}")]
    public async Task<IActionResult> Create(int reportId, CancellationToken cancellationToken)
    {
        if (!await CanManageReportAsync(reportId, cancellationToken))
        {
            return NotFound();
        }

        var model = await BuildViewModelAsync(reportId, cancellationToken: cancellationToken);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("create/{reportId:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        int reportId,
        ActivityAssignmentCreateViewModel model,
        CancellationToken cancellationToken)
    {
        if (!await CanManageReportAsync(reportId, cancellationToken))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildViewModelAsync(reportId, model.LearningActivityId, cancellationToken);
            return invalidModel is null ? NotFound() : View(invalidModel);
        }

        var assignedByUserId = userManager.GetUserId(User);
        if (assignedByUserId is null)
        {
            return Forbid();
        }

        try
        {
            await learningActivityService.AssignAsync(
                reportId,
                model.LearningActivityId,
                assignedByUserId,
                cancellationToken);
        }
        catch (ActivityAssignmentException exception)
        {
            ModelState.AddModelError(nameof(model.LearningActivityId), exception.Message);
            var invalidModel = await BuildViewModelAsync(reportId, model.LearningActivityId, cancellationToken);
            return invalidModel is null ? NotFound() : View(invalidModel);
        }

        TempData["StatusMessage"] = "The learning activity was assigned. Existing assignments and professional decisions were left unchanged.";
        return RedirectToAction(nameof(Create), new { reportId });
    }

    private async Task<bool> CanManageReportAsync(int reportId, CancellationToken cancellationToken)
    {
        var reportIsAvailable = await dbContext.ScreeningReports.AnyAsync(report =>
            report.Id == reportId &&
            report.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
            report.ScreeningAttempt.CompletedAtUtc != null,
            cancellationToken);
        if (!reportIsAvailable)
        {
            return false;
        }

        if (User.IsInRole(ApplicationRoles.Administrator))
        {
            return true;
        }

        var psychologistId = userManager.GetUserId(User);
        return psychologistId is not null && await dbContext.Referrals.AnyAsync(referral =>
            referral.ScreeningReportId == reportId &&
            referral.AssignedPsychologistId == psychologistId &&
            referral.LearnerId == referral.ScreeningReport.UserId &&
            referral.ScreeningReport.RiskTier == nameof(ScreeningSupportTier.Red) &&
            referral.ScreeningReport.IsReferralRecommended &&
            referral.ScreeningReport.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
            referral.ScreeningReport.ScreeningAttempt.CompletedAtUtc != null,
            cancellationToken);
    }

    private async Task<ActivityAssignmentCreateViewModel?> BuildViewModelAsync(
        int reportId,
        int selectedActivityId = 0,
        CancellationToken cancellationToken = default)
    {
        var report = await dbContext.ScreeningReports
            .AsNoTracking()
            .Include(item => item.User)
            .SingleOrDefaultAsync(item => item.Id == reportId &&
                                          item.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                                          item.ScreeningAttempt.CompletedAtUtc != null,
                cancellationToken);
        if (report is null)
        {
            return null;
        }

        var activities = await dbContext.LearningActivities
            .AsNoTracking()
            .Where(activity => activity.IsActive)
            .OrderBy(activity => activity.Name)
            .Select(activity => new ActivityOptionViewModel(activity.Id, activity.Name, activity.ActivityType))
            .ToListAsync(cancellationToken);
        var assignments = await dbContext.ActivityAssignments
            .AsNoTracking()
            .Where(assignment => assignment.RelatedScreeningReportId == reportId && assignment.LearnerId == report.UserId)
            .OrderByDescending(assignment => assignment.AssignedAtUtc)
            .Select(assignment => new ActivityAssignmentListItemViewModel(
                assignment.LearningActivity.Name,
                assignment.AssignedAtUtc,
                assignment.Status))
            .ToListAsync(cancellationToken);
        var recommendations = await learningActivityService.GetRecommendationsAsync(reportId, cancellationToken);

        return new ActivityAssignmentCreateViewModel
        {
            ReportId = report.Id,
            LearnerName = string.IsNullOrWhiteSpace(report.User.PreferredName)
                ? $"{report.User.FirstName} {report.User.LastName}".Trim()
                : report.User.PreferredName,
            ReportGeneratedAtUtc = report.GeneratedAtUtc,
            FlaggedAreas = ReadFlaggedAreaNames(report.FlaggedAreasJson),
            Recommendations = recommendations,
            ActiveActivities = activities,
            ExistingAssignments = assignments,
            LearningActivityId = selectedActivityId
        };
    }

    private static IReadOnlyList<string> ReadFlaggedAreaNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ScreeningReportFlaggedAreaSnapshot>>(json, JsonOptions)?
                .Select(area => area.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
