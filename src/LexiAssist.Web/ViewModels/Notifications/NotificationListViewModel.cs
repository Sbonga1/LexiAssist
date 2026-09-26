using LexiAssist.Web.Models.Notifications;

namespace LexiAssist.Web.ViewModels.Notifications;

public sealed class NotificationListViewModel
{
    public IReadOnlyList<NotificationListItemViewModel> Notifications { get; init; } = [];

    public int CurrentPage { get; init; }

    public int PageSize { get; init; }

    public int TotalItems { get; init; }

    public int TotalPages { get; init; }

    public bool HasPreviousPage => CurrentPage > 1;

    public bool HasNextPage => CurrentPage < TotalPages;

    public int FirstItemNumber => TotalItems == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;

    public int LastItemNumber => TotalItems == 0 ? 0 : Math.Min(CurrentPage * PageSize, TotalItems);
}

public sealed record NotificationListItemViewModel(
    int Id,
    AppNotificationType Type,
    string Title,
    string Message,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    string? RelatedUrl);
