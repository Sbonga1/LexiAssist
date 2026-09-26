using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.ViewModels.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Controllers;

[Authorize]
public sealed class NotificationsController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : Controller
{
    private const int PageSize = 5;

    [HttpGet]
    public async Task<IActionResult> Index(int? page, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var notificationsQuery = dbContext.AppNotifications
            .AsNoTracking()
            .Where(notification => notification.RecipientUserId == userId)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id);

        var totalItems = await notificationsQuery.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)PageSize));
        var currentPage = Math.Clamp(page.GetValueOrDefault(1), 1, totalPages);

        var notifications = await notificationsQuery
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(notification => new NotificationListItemViewModel(
                notification.Id,
                notification.Type,
                notification.Title,
                notification.Message,
                notification.CreatedAtUtc,
                notification.ReadAtUtc,
                notification.RelatedUrl))
            .ToListAsync(cancellationToken);

        var safeNotifications = notifications
            .Select(notification => notification.RelatedUrl is null || Url.IsLocalUrl(notification.RelatedUrl)
                ? notification
                : notification with { RelatedUrl = null })
            .ToArray();

        return View(new NotificationListViewModel
        {
            Notifications = safeNotifications,
            CurrentPage = currentPage,
            PageSize = PageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Open(int id, CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        if (userId is null)
        {
            return Forbid();
        }

        var notification = await dbContext.AppNotifications
            .SingleOrDefaultAsync(item => item.Id == id && item.RecipientUserId == userId, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        if (notification.ReadAtUtc is null)
        {
            notification.ReadAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return !string.IsNullOrWhiteSpace(notification.RelatedUrl) && Url.IsLocalUrl(notification.RelatedUrl)
            ? LocalRedirect(notification.RelatedUrl)
            : RedirectToAction(nameof(Index))!;
    }
}
