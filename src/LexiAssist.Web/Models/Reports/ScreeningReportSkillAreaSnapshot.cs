namespace LexiAssist.Web.Models.Reports;

using LexiAssist.Web.Models.Screening;

public sealed record ScreeningReportSkillAreaSnapshot(
    string Name,
    decimal? PercentageScore,
    bool IsStrength,
    bool IsFlaggedForSupport,
    string Summary)
{
    public IReadOnlyList<ScreeningActivityMetric> ActivityMetrics { get; init; } = [];
}

public sealed record ScreeningReportFlaggedAreaSnapshot(string Name, decimal? PercentageScore);
