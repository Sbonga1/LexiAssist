using LexiAssist.Web.Models.Reports;

namespace LexiAssist.Web.ViewModels.AdminScreeningReports;

public sealed class AdminScreeningReportDetailsViewModel
{
    public int Id { get; init; }

    public string LearnerName { get; init; } = string.Empty;

    public string LearnerEmail { get; init; } = string.Empty;

    public DateTime GeneratedAtUtc { get; init; }

    public string? RiskTier { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string RecommendedNextAction { get; init; } = string.Empty;

    public bool IsReferralRecommended { get; init; }

    public string ScoringVersion { get; init; } = string.Empty;

    public IReadOnlyList<ScreeningReportSkillAreaSnapshot> SkillAreas { get; init; } = [];
}
