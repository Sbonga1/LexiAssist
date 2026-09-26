namespace LexiAssist.Web.ViewModels.AdminScreeningReports;

public sealed class AdminScreeningReportsIndexViewModel
{
    public IReadOnlyList<AdminScreeningReportListItemViewModel> Reports { get; init; } = [];

    public string? SearchLearner { get; init; }

    public DateOnly? FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    public string? RiskTier { get; init; }

    public int PageNumber { get; init; }

    public int TotalPages { get; init; }

    public int TotalCount { get; init; }

    public int PageSize { get; init; }

    public int FirstItemNumber => TotalCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;

    public int LastItemNumber => TotalCount == 0 ? 0 : Math.Min(PageNumber * PageSize, TotalCount);

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}

public sealed record AdminScreeningReportListItemViewModel(
    int Id,
    string LearnerName,
    DateTime GeneratedAtUtc,
    string? RiskTier);
