using LexiAssist.Web.Models.Referrals;
using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.ViewModels.Referrals;

public sealed class AdminReferralsIndexViewModel
{
    public IReadOnlyList<AdminReferralListItemViewModel> Referrals { get; init; } = [];

    public string? SearchLearner { get; init; }

    public ReferralStatus? StatusFilter { get; init; }

    public int PageNumber { get; init; }

    public int TotalPages { get; init; }

    public int TotalCount { get; init; }

    public int PageSize { get; init; }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;

    public int FirstItemNumber => TotalCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;

    public int LastItemNumber => TotalCount == 0 ? 0 : Math.Min(PageNumber * PageSize, TotalCount);
}

public sealed record AdminReferralListItemViewModel(
    int Id,
    string LearnerName,
    string? LearnerEmail,
    DateTime CreatedAtUtc,
    ReferralStatus Status,
    string? AssignedPsychologistName);

public sealed class AdminReferralDetailsViewModel
{
    public int Id { get; init; }

    public int ScreeningReportId { get; init; }

    public string LearnerName { get; init; } = string.Empty;

    public string LearnerEmail { get; init; } = string.Empty;

    public string? RiskTier { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? ReviewedAtUtc { get; init; }

    public ReferralStatus Status { get; init; }

    public string? Notes { get; init; }

    public string? AssignedPsychologistId { get; init; }

    public IReadOnlyList<ReferralPsychologistOptionViewModel> Psychologists { get; init; } = [];

    public bool CanAssign => Status is ReferralStatus.Pending or ReferralStatus.Assigned;
}

public sealed record ReferralPsychologistOptionViewModel(string Id, string DisplayName);

public sealed class AdminReferralAssignViewModel
{
    [Required]
    public string PsychologistId { get; set; } = string.Empty;
}
