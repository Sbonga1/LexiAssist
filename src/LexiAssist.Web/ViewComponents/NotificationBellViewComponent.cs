using System.Security.Claims;
using LexiAssist.Web.Data;
using LexiAssist.Web.ViewModels.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.ViewComponents;

public sealed class NotificationBellViewComponent(ApplicationDbContext dbContext) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(CancellationToken cancellationToken = default)
    {
        var userId = UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Content(string.Empty);
        }

        var unreadCount = await dbContext.AppNotifications
            .AsNoTracking()
            .CountAsync(notification => notification.RecipientUserId == userId && notification.ReadAtUtc == null, cancellationToken);

        return View(new NotificationBellViewModel(unreadCount));
    }
}
