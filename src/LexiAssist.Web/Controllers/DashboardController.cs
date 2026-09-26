using System.Security.Claims;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize]
public sealed class DashboardController(
    ApplicationDbContext dbContext,
    IUserReminderPreferenceService reminderPreferenceService,
    TimeProvider timeProvider,
    UserManager<ApplicationUser> userManager,
    IAdministratorAlertService administratorAlertService) : Controller
{
    [Authorize(Roles = ApplicationRoles.Learner)]
    public async Task<IActionResult> Learner(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var mostRecentResult = await dbContext.ScreeningAttempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == userId && attempt.Status == ScreeningAttemptStatus.Completed)
            .OrderByDescending(attempt => attempt.CompletedAtUtc)
            .Select(attempt => new RecentScreeningResultViewModel(
                attempt.Id,
                attempt.CompletedAtUtc ?? attempt.StartedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        var reminderPreference = await reminderPreferenceService.GetForUserAsync(userId, cancellationToken);
        var todayStartUtc = timeProvider.GetUtcNow().UtcDateTime.Date;
        var tomorrowStartUtc = todayStartUtc.AddDays(1);
        var totalDurationMilliseconds = await dbContext.ActivityAttempts
            .AsNoTracking()
            .Where(attempt => attempt.ActivityAssignment.LearnerId == userId &&
                              attempt.ActivityAssignment.Status != ActivityAssignmentStatus.Cancelled &&
                              attempt.StartedAtUtc >= todayStartUtc &&
                              attempt.StartedAtUtc < tomorrowStartUtc)
            .Select(attempt => (long?)(attempt.DurationMilliseconds ?? 0))
            .SumAsync(cancellationToken) ?? 0L;
        var completedMinutes = (int)Math.Min(int.MaxValue, totalDurationMilliseconds / 60_000L);

        var dashboard = new LearnerDashboardViewModel(mostRecentResult)
        {
            TodayPractice = new TodayPracticeViewModel(
                reminderPreference.DailyPracticeTargetMinutes,
                completedMinutes,
                reminderPreference.RemindersEnabled,
                reminderPreference.PreferredReminderTime)
        };
        return View(dashboard);
    }

    [Authorize(Roles = ApplicationRoles.Administrator)]
    public async Task<IActionResult> Administrator(CancellationToken cancellationToken)
    {
        var administratorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (administratorId is null)
        {
            return Unauthorized();
        }

        var learners = await userManager.GetUsersInRoleAsync(ApplicationRoles.Learner);
        var activeLearnerCount = learners.Count(learner => learner.IsActive);
        var completedScreeningCount = await dbContext.ScreeningAttempts
            .AsNoTracking()
            .CountAsync(attempt => attempt.Status == ScreeningAttemptStatus.Completed, cancellationToken);
        var pendingReferralCount = await dbContext.Referrals
            .AsNoTracking()
            .CountAsync(referral => referral.LearnerId == referral.ScreeningReport.UserId &&
                                    referral.ScreeningReport.RiskTier == nameof(ScreeningSupportTier.Red) &&
                                    referral.ScreeningReport.IsReferralRecommended &&
                                    referral.ScreeningReport.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                                    referral.ScreeningReport.ScreeningAttempt.CompletedAtUtc != null &&
                                    referral.Status == ReferralStatus.Pending, cancellationToken);
        var assignedReferralCount = await dbContext.Referrals
            .AsNoTracking()
            .CountAsync(referral => referral.LearnerId == referral.ScreeningReport.UserId &&
                                    referral.ScreeningReport.RiskTier == nameof(ScreeningSupportTier.Red) &&
                                    referral.ScreeningReport.IsReferralRecommended &&
                                    referral.ScreeningReport.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                                    referral.ScreeningReport.ScreeningAttempt.CompletedAtUtc != null &&
                                    referral.AssignedPsychologistId != null, cancellationToken);
        var activeLearningActivityCount = await dbContext.LearningActivities
            .AsNoTracking()
            .CountAsync(activity => activity.IsActive, cancellationToken);
        var recentSystemAlerts = (await administratorAlertService
                .GetForAdministratorAsync(administratorId, cancellationToken))
            .Select(alert => new AdministratorSystemAlertViewModel(
                alert.Id ?? 0,
                alert.Title,
                alert.Message,
                alert.CreatedAtUtc,
                alert.ReadAtUtc,
                alert.RelatedUrl,
                alert.Kind,
                alert.IsGenerated))
            .ToArray();

        return View(new AdministratorDashboardViewModel(
            activeLearnerCount,
            completedScreeningCount,
            pendingReferralCount,
            assignedReferralCount,
            activeLearningActivityCount,
            recentSystemAlerts));
    }

    [Authorize(Roles = ApplicationRoles.Psychologist)]
    public async Task<IActionResult> Psychologist(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var assignedReferrals = AssignedReferrals(userId);
        var assignedPending = await ToDashboardRowsAsync(
            assignedReferrals
                .Where(referral => referral.Status == ReferralStatus.Assigned)
                .OrderByDescending(referral => referral.CreatedAtUtc),
            cancellationToken);
        var underReview = await ToDashboardRowsAsync(
            assignedReferrals
                .Where(referral => referral.Status == ReferralStatus.UnderReview)
                .OrderByDescending(referral => referral.ReviewedAtUtc ?? referral.CreatedAtUtc),
            cancellationToken);
        var recentlyReviewed = await ToDashboardRowsAsync(
            assignedReferrals
                .Where(referral => referral.ReviewedAtUtc != null &&
                                   (referral.Status == ReferralStatus.FollowUpRequested ||
                                    referral.Status == ReferralStatus.Completed ||
                                    referral.Status == ReferralStatus.Closed))
                .OrderByDescending(referral => referral.ReviewedAtUtc)
                .Take(5),
            cancellationToken);

        return View(new PsychologistDashboardViewModel(assignedPending, underReview, recentlyReviewed));
    }

    private IQueryable<Referral> AssignedReferrals(string psychologistId) => dbContext.Referrals
        .AsNoTracking()
        .Where(referral => referral.AssignedPsychologistId == psychologistId &&
                           referral.LearnerId == referral.ScreeningReport.UserId &&
                           referral.ScreeningReport.RiskTier == nameof(ScreeningSupportTier.Red) &&
                           referral.ScreeningReport.IsReferralRecommended &&
                           referral.ScreeningReport.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                           referral.ScreeningReport.ScreeningAttempt.CompletedAtUtc != null);

    private static async Task<IReadOnlyList<PsychologistDashboardReferralViewModel>> ToDashboardRowsAsync(
        IQueryable<Referral> referrals,
        CancellationToken cancellationToken)
    {
        var rows = await referrals
            .Select(referral => new
            {
                referral.Id,
                FirstName = referral.Learner.FirstName,
                LastName = referral.Learner.LastName,
                PreferredName = referral.Learner.PreferredName,
                referral.ScreeningReport.RiskTier,
                ReferralDateUtc = referral.CreatedAtUtc,
                referral.ReviewedAtUtc,
                referral.Status
            })
            .ToListAsync(cancellationToken);

        return rows.Select(referral => new PsychologistDashboardReferralViewModel(
            referral.Id,
            string.IsNullOrWhiteSpace(referral.PreferredName)
                ? $"{referral.FirstName} {referral.LastName}".Trim()
                : referral.PreferredName,
            referral.RiskTier,
            referral.ReferralDateUtc,
            referral.ReviewedAtUtc,
            referral.Status))
            .ToArray();
    }
}
