using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Notifications;

namespace LexiAssist.Web.Services;

public sealed class AppNotificationService(ApplicationDbContext dbContext, TimeProvider timeProvider)
{
    public AppNotification Add(
        string recipientUserId,
        AppNotificationType type,
        string title,
        string message,
        string? relatedUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (relatedUrl is not null &&
            (!relatedUrl.StartsWith("/", StringComparison.Ordinal) ||
             relatedUrl.StartsWith("//", StringComparison.Ordinal) ||
             relatedUrl.Contains('\\') ||
             relatedUrl.Contains('%') ||
             relatedUrl.Any(char.IsControl)))
        {
            throw new ArgumentException("Notification links must be local application paths.", nameof(relatedUrl));
        }

        var notification = new AppNotification
        {
            RecipientUserId = recipientUserId,
            Type = type,
            Title = title.Trim(),
            Message = message.Trim(),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            RelatedUrl = relatedUrl
        };
        dbContext.AppNotifications.Add(notification);
        return notification;
    }

    public AppNotification AddLearningActivityAssigned(string learnerUserId, string activityTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityTitle);
        return Add(
            learnerUserId,
            AppNotificationType.LearningActivityAssigned,
            "A learning activity was assigned",
            $"A learning activity has been assigned: {activityTitle.Trim()}.",
            "/Dashboard/Learner#practice");
    }
}
