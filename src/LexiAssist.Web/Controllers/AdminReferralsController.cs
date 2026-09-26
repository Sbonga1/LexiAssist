using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Audit;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Models.Notifications;
using LexiAssist.Web.Services;
using LexiAssist.Web.Services.Interfaces;
using LexiAssist.Web.ViewModels.Referrals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("admin/referrals")]
public sealed class AdminReferralsController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    AppNotificationService appNotificationService,
    IAuditService auditService) : Controller
{
    private const int PageSize = 5;

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "search")] string? search,
        [FromQuery(Name = "searchLearner")] string? legacySearch,
        string? status,
        int? page,
        CancellationToken cancellationToken = default)
    {
        var searchTerm = NormalizeFilter(search ?? legacySearch, 100);

        var statusFilter = ParseStatus(status);
        var referrals = EligibleReferrals().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            referrals = referrals.Where(referral =>
                referral.Learner.FirstName.Contains(searchTerm) ||
                referral.Learner.LastName.Contains(searchTerm) ||
                (referral.Learner.FirstName + " " + referral.Learner.LastName).Contains(searchTerm) ||
                (referral.Learner.PreferredName != null && referral.Learner.PreferredName.Contains(searchTerm)) ||
                (referral.Learner.Email != null && referral.Learner.Email.Contains(searchTerm)));
        }

        if (statusFilter.HasValue)
        {
            referrals = referrals.Where(referral => referral.Status == statusFilter.Value);
        }

        var totalCount = await referrals.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        var pageNumber = Math.Clamp(page.GetValueOrDefault(1), 1, totalPages);
        var referralRows = await referrals
            .OrderByDescending(referral => referral.CreatedAtUtc)
            .ThenByDescending(referral => referral.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(referral => new
            {
                referral.Id,
                LearnerFirstName = referral.Learner.FirstName,
                LearnerLastName = referral.Learner.LastName,
                LearnerPreferredName = referral.Learner.PreferredName,
                LearnerEmail = referral.Learner.Email,
                referral.CreatedAtUtc,
                referral.Status,
                PsychologistFirstName = referral.AssignedPsychologist == null ? null : referral.AssignedPsychologist.FirstName,
                PsychologistLastName = referral.AssignedPsychologist == null ? null : referral.AssignedPsychologist.LastName,
                PsychologistPreferredName = referral.AssignedPsychologist == null ? null : referral.AssignedPsychologist.PreferredName
            })
            .ToListAsync(cancellationToken);
        var rows = referralRows.Select(referral => new AdminReferralListItemViewModel(
            referral.Id,
            LearnerName(referral.LearnerFirstName, referral.LearnerLastName, referral.LearnerPreferredName),
            referral.LearnerEmail,
            referral.CreatedAtUtc,
            referral.Status,
            referral.PsychologistFirstName is null
                ? null
                : LearnerName(referral.PsychologistFirstName, referral.PsychologistLastName!, referral.PsychologistPreferredName)))
            .ToArray();

        return View(new AdminReferralsIndexViewModel
        {
            Referrals = rows,
            SearchLearner = searchTerm,
            StatusFilter = statusFilter,
            PageNumber = pageNumber,
            TotalPages = totalPages,
            TotalCount = totalCount,
            PageSize = PageSize
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var model = await BuildDetailsViewModelAsync(id);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("{id:int}/assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Assign(int id, AdminReferralAssignViewModel model)
    {
        var referral = await EligibleReferrals()
            .Include(item => item.ScreeningReport)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (referral is null)
        {
            return NotFound();
        }

        if (referral.Status is not ReferralStatus.Pending and not ReferralStatus.Assigned)
        {
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.Referral, id.ToString(), false,
                new { Operation = "Assign", Reason = "InvalidStatus" });
            TempData["ErrorMessage"] = "This referral can no longer be assigned from its current status.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var psychologist = await userManager.FindByIdAsync(model.PsychologistId);
        if (psychologist is null || !psychologist.IsActive ||
            !await userManager.IsInRoleAsync(psychologist, ApplicationRoles.Psychologist))
        {
            ModelState.AddModelError(nameof(model.PsychologistId), "Choose an active Psychologist account.");
        }

        if (!ModelState.IsValid)
        {
            await auditService.RecordAsync(AuditActions.AdministrativeActionFailed, AuditEntityTypes.Referral, id.ToString(), false,
                new { Operation = "Assign", Reason = "InvalidPsychologist" });
            var viewModel = await BuildDetailsViewModelAsync(id);
            return viewModel is null ? NotFound() : View("Details", viewModel);
        }

        var assignmentChanged = !string.Equals(referral.AssignedPsychologistId, psychologist!.Id, StringComparison.Ordinal);
        referral.AssignedPsychologistId = psychologist.Id;
        referral.Status = ReferralStatus.Assigned;
        if (assignmentChanged)
        {
            appNotificationService.Add(
                psychologist.Id,
                AppNotificationType.ReferralAssigned,
                "A referral was assigned to you",
                "A learner screening referral has been assigned to you for professional review.",
                $"/psychologist/referrals/{referral.Id}");
            appNotificationService.Add(
                referral.LearnerId,
                AppNotificationType.ReferralAssigned,
                "A Psychologist was assigned",
                "A Psychologist has been assigned to review the screening referral. You can review your screening summary and suggested next steps.",
                $"/screening/results/{referral.ScreeningReport.ScreeningAttemptId}");
        }

        await dbContext.SaveChangesAsync();
        await auditService.RecordAsync(AuditActions.ReferralAssigned, AuditEntityTypes.Referral, referral.Id.ToString(), true,
            new { PsychologistId = psychologist.Id });
        TempData["StatusMessage"] = "The referral was assigned to the Psychologist.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private IQueryable<Referral> EligibleReferrals() => dbContext.Referrals
        .Where(referral => referral.LearnerId == referral.ScreeningReport.UserId &&
                           referral.ScreeningReport.RiskTier == nameof(ScreeningSupportTier.Red) &&
                           referral.ScreeningReport.IsReferralRecommended &&
                           referral.ScreeningReport.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                           referral.ScreeningReport.ScreeningAttempt.CompletedAtUtc != null);

    private async Task<AdminReferralDetailsViewModel?> BuildDetailsViewModelAsync(int id)
    {
        var referral = await EligibleReferrals()
            .AsNoTracking()
            .Include(item => item.Learner)
            .Include(item => item.AssignedPsychologist)
            .Include(item => item.ScreeningReport)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (referral is null)
        {
            return null;
        }

        var psychologists = await userManager.GetUsersInRoleAsync(ApplicationRoles.Psychologist);
        return new AdminReferralDetailsViewModel
        {
            Id = referral.Id,
            ScreeningReportId = referral.ScreeningReportId,
            LearnerName = LearnerName(referral.Learner),
            LearnerEmail = referral.Learner.Email ?? string.Empty,
            RiskTier = referral.ScreeningReport.RiskTier,
            CreatedAtUtc = referral.CreatedAtUtc,
            ReviewedAtUtc = referral.ReviewedAtUtc,
            Status = referral.Status,
            Notes = referral.Notes,
            AssignedPsychologistId = referral.AssignedPsychologistId,
            Psychologists = psychologists
                .Where(user => user.IsActive)
                .OrderBy(user => user.LastName)
                .ThenBy(user => user.FirstName)
                .Select(user => new ReferralPsychologistOptionViewModel(user.Id, LearnerName(user)))
                .ToArray()
        };
    }

    private static ReferralStatus? ParseStatus(string? value) =>
        Enum.TryParse<ReferralStatus>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static string? NormalizeFilter(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (normalized?.Length > maxLength)
        {
            normalized = normalized[..maxLength];
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string LearnerName(ApplicationUser user) =>
        LearnerName(user.FirstName, user.LastName, user.PreferredName);

    private static string LearnerName(string firstName, string lastName, string? preferredName) =>
        string.IsNullOrWhiteSpace(preferredName) ? $"{firstName} {lastName}".Trim() : preferredName;
}
