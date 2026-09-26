namespace LexiAssist.Web.Models.Notifications;

/// <summary>
/// Categories of administrator attention signals. Attendance is reserved for a future
/// integration and is not produced while LexiAssist has no attendance data source.
/// </summary>
public enum AdministratorAlertKind
{
    Notification = 0,
    ScreeningAttention = 1,
    ReferralAgeing = 2,
    TechnicalFailure = 3,
    ContentConfiguration = 4,
    Attendance = 5
}
