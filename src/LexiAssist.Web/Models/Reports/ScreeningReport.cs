using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Models.Reports;

public class ScreeningReport
{
    public int Id { get; set; }

    public int ScreeningAttemptId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTime GeneratedAtUtc { get; set; }

    public string? RiskTier { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string FlaggedAreasJson { get; set; } = "[]";

    public string ScoringVersion { get; set; } = string.Empty;

    public bool IsReferralRecommended { get; set; }

    public string SkillAreasJson { get; set; } = "[]";

    public string RecommendedNextAction { get; set; } = string.Empty;

    public ScreeningAttempt ScreeningAttempt { get; set; } = null!;

    public ApplicationUser User { get; set; } = null!;

    public Referral? Referral { get; set; }
}
