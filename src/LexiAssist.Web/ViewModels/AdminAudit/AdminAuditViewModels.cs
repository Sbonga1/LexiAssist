namespace LexiAssist.Web.ViewModels.AdminAudit;

public sealed class AdminAuditIndexViewModel
{
    public IReadOnlyList<AdminAuditEventListItemViewModel> Events { get; init; } = [];
    public string? ActionFilter { get; init; }
    public string? EntityTypeFilter { get; init; }
    public bool? SucceededFilter { get; init; }
    public int PageNumber { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public int PageSize { get; init; }
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public int FirstItemNumber => TotalCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
    public int LastItemNumber => TotalCount == 0 ? 0 : Math.Min(PageNumber * PageSize, TotalCount);
}

public sealed record AdminAuditEventListItemViewModel(
    long Id,
    DateTime OccurredAtUtc,
    string ActorName,
    string Action,
    string EntityType,
    string? EntityId,
    bool Succeeded,
    string? DetailsJson);
