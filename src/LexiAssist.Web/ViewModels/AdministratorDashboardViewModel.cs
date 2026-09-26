using LexiAssist.Web.Models.Notifications;

namespace LexiAssist.Web.ViewModels;

public sealed record AdministratorDashboardViewModel(
    int ActiveLearnerCount,
    int CompletedScreeningCount,
    int PendingReferralCount,
    int AssignedReferralCount,
    int ActiveLearningActivityCount,
    IReadOnlyList<AdministratorSystemAlertViewModel> RecentSystemAlerts);

public sealed record AdministratorSystemAlertViewModel(
    int Id,
    string Title,
    string Message,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    string? RelatedUrl,
    AdministratorAlertKind Kind = AdministratorAlertKind.Notification,
    bool IsGenerated = false);
