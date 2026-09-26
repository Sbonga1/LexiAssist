using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.ViewModels.AdminAudit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize(Roles = ApplicationRoles.Administrator)]
[Route("admin/audit")]
public sealed class AdminAuditController(ApplicationDbContext dbContext) : Controller
{
    private const int PageSize = 5;

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "action")] string? actionFilterInput,
        string? entityType,
        bool? succeeded,
        int? page = null,
        CancellationToken cancellationToken = default)
    {
        var actionFilter = NormalizeFilter(actionFilterInput, 120);
        var entityTypeFilter = NormalizeFilter(entityType, 120);
        var events = dbContext.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            events = events.Where(item => item.Action.Contains(actionFilter));
        }

        if (!string.IsNullOrWhiteSpace(entityTypeFilter))
        {
            events = events.Where(item => item.EntityType.Contains(entityTypeFilter));
        }

        if (succeeded.HasValue)
        {
            events = events.Where(item => item.Succeeded == succeeded.Value);
        }

        var totalCount = await events.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));
        var pageNumber = Math.Clamp(page.GetValueOrDefault(1), 1, totalPages);
        var rows = await events
            .OrderByDescending(item => item.OccurredAtUtc)
            .ThenByDescending(item => item.Id)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(item => new AdminAuditEventListItemViewModel(
                item.Id,
                item.OccurredAtUtc,
                item.Actor == null
                    ? "System or anonymous"
                    : (string.IsNullOrWhiteSpace(item.Actor.PreferredName)
                        ? (item.Actor.FirstName + " " + item.Actor.LastName).Trim()
                        : item.Actor.PreferredName!),
                item.Action,
                item.EntityType,
                item.EntityId,
                item.Succeeded,
                item.DetailsJson))
            .ToListAsync(cancellationToken);

        return View(new AdminAuditIndexViewModel
        {
            Events = rows,
            ActionFilter = actionFilter,
            EntityTypeFilter = entityTypeFilter,
            SucceededFilter = succeeded,
            PageNumber = pageNumber,
            TotalPages = totalPages,
            TotalCount = totalCount,
            PageSize = PageSize
        });
    }

    private static string? NormalizeFilter(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (normalized?.Length > maxLength)
        {
            normalized = normalized[..maxLength];
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
