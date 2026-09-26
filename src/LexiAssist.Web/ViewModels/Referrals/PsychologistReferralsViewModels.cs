using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Reports;

namespace LexiAssist.Web.ViewModels.Referrals;

public sealed class PsychologistReferralsIndexViewModel
{
    public IReadOnlyList<PsychologistReferralListItemViewModel> Referrals { get; init; } = [];

    public int PageNumber { get; init; }

    public int TotalPages { get; init; }

    public int TotalCount { get; init; }

    public bool HasPreviousPage => PageNumber > 1;

    public bool HasNextPage => PageNumber < TotalPages;
}

public sealed record PsychologistReferralListItemViewModel(
    int Id,
    string LearnerName,
    DateTime CreatedAtUtc,
    ReferralStatus Status);

public sealed class PsychologistReferralDetailsViewModel
{
    public int Id { get; init; }

    public int ScreeningReportId { get; init; }

    public string LearnerName { get; init; } = string.Empty;

    public string LearnerEmail { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? ReviewedAtUtc { get; init; }

    public ReferralStatus Status { get; init; }

    public string? Notes { get; init; }

    public string? RiskTier { get; init; }

    public string ReportSummary { get; init; } = string.Empty;

    public IReadOnlyList<ScreeningReportSkillAreaSnapshot> SkillAreas { get; init; } = [];

    public IReadOnlyList<ReferralStatus> AllowedStatuses { get; init; } = [];

    public bool IsFinal => Status is ReferralStatus.Completed or ReferralStatus.Closed;
}

public sealed class PsychologistReferralUpdateViewModel
{
    public ReferralStatus Status { get; init; }

    [System.ComponentModel.DataAnnotations.StringLength(4000)]
    public string? Notes { get; init; }
}
