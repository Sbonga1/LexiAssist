using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;

namespace LexiAssist.Web.Models.Screening;

public class ScreeningAttempt
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public ScreeningAttemptStatus Status { get; set; } = ScreeningAttemptStatus.InProgress;

    public double? OverallScore { get; set; }

    public string? RiskTier { get; set; }

    public string? RiskCalculationVersion { get; set; }

    public DateTime? RiskCalculatedAtUtc { get; set; }

    public int? CurrentTestId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public ScreeningTest? CurrentTest { get; set; }

    public ICollection<ScreeningTestAttempt> TestAttempts { get; set; } = new List<ScreeningTestAttempt>();

    public ScreeningReport? Report { get; set; }
}
