using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Services;
using LexiAssist.Web.ViewModels.AdminScreeningReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("admin/screening-reports")]
public sealed class AdminScreeningReportsController(ApplicationDbContext dbContext) : Controller
{
    private const int PageSize = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? searchLearner,
        DateOnly? fromDate,
        DateOnly? toDate,
        string? riskTier,
        int? page = null,
        CancellationToken cancellationToken = default)
    {
        var searchTerm = searchLearner?.Trim();
        if (searchTerm?.Length > 100)
        {
            searchTerm = searchTerm[..100];
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
        {
            ModelState.AddModelError(string.Empty, "The start date must be on or before the end date.");
            fromDate = null;
            toDate = null;
        }

        var selectedTier = NormalizeTier(riskTier);
        var reports = dbContext.ScreeningReports
            .AsNoTracking()
            .Where(report => report.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                             report.ScreeningAttempt.CompletedAtUtc != null);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            reports = reports.Where(report =>
                report.User.FirstName.Contains(searchTerm) ||
                report.User.LastName.Contains(searchTerm) ||
                (report.User.PreferredName != null && report.User.PreferredName.Contains(searchTerm)) ||
                (report.User.Email != null && report.User.Email.Contains(searchTerm)));
        }

        if (fromDate.HasValue)
        {
            var fromUtc = SouthAfricaTimeFormatter.ToUtcStartOfDay(fromDate.Value);
            reports = reports.Where(report => report.GeneratedAtUtc >= fromUtc);
        }

        if (toDate.HasValue && toDate.Value < DateOnly.MaxValue)
        {
            var untilUtc = SouthAfricaTimeFormatter.ToUtcStartOfDay(toDate.Value.AddDays(1));
            reports = reports.Where(report => report.GeneratedAtUtc < untilUtc);
        }

        if (selectedTier == "Unclassified")
        {
            reports = reports.Where(report => report.RiskTier == null || report.RiskTier == string.Empty);
        }
        else if (selectedTier is not null)
        {
            reports = reports.Where(report => report.RiskTier == selectedTier);
        }

        var totalCount = await reports.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        var pageNumber = Math.Clamp(page.GetValueOrDefault(1), 1, totalPages);
        var pageReports = await reports
            .OrderByDescending(report => report.GeneratedAtUtc)
            .ThenByDescending(report => report.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(report => new AdminScreeningReportListItemViewModel(
                report.Id,
                string.IsNullOrWhiteSpace(report.User.PreferredName)
                    ? $"{report.User.FirstName} {report.User.LastName}".Trim()
                    : report.User.PreferredName!,
                report.GeneratedAtUtc,
                report.RiskTier))
            .ToListAsync(cancellationToken);

        return View(new AdminScreeningReportsIndexViewModel
        {
            Reports = pageReports,
            SearchLearner = searchTerm,
            FromDate = fromDate,
            ToDate = toDate,
            RiskTier = selectedTier,
            PageNumber = pageNumber,
            TotalPages = totalPages,
            TotalCount = totalCount,
            PageSize = PageSize
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var report = await dbContext.ScreeningReports
            .AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.ScreeningAttempt)
            .SingleOrDefaultAsync(item => item.Id == id &&
                                          item.ScreeningAttempt.Status == ScreeningAttemptStatus.Completed &&
                                          item.ScreeningAttempt.CompletedAtUtc != null);

        if (report is null)
        {
            return NotFound();
        }

        return View(new AdminScreeningReportDetailsViewModel
        {
            Id = report.Id,
            LearnerName = string.IsNullOrWhiteSpace(report.User.PreferredName)
                ? $"{report.User.FirstName} {report.User.LastName}".Trim()
                : report.User.PreferredName!,
            LearnerEmail = report.User.Email ?? string.Empty,
            GeneratedAtUtc = report.GeneratedAtUtc,
            RiskTier = report.RiskTier,
            Summary = report.Summary,
            RecommendedNextAction = report.RecommendedNextAction,
            IsReferralRecommended = report.IsReferralRecommended,
            ScoringVersion = report.ScoringVersion,
            SkillAreas = ReadSkillAreas(report.SkillAreasJson)
        });
    }

    private static string? NormalizeTier(string? riskTier)
    {
        var candidate = riskTier?.Trim();
        return candidate is "Green" or "Amber" or "Red" or "Unclassified" ? candidate : null;
    }

    private static IReadOnlyList<ScreeningReportSkillAreaSnapshot> ReadSkillAreas(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ScreeningReportSkillAreaSnapshot>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
