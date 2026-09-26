using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;

namespace LexiAssist.Web.Models.Referrals;

public class Referral
{
    public int Id { get; set; }

    public int ScreeningReportId { get; set; }

    public string LearnerId { get; set; } = string.Empty;

    public string? AssignedPsychologistId { get; set; }

    public ReferralStatus Status { get; set; } = ReferralStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAtUtc { get; set; }

    public string? Notes { get; set; }

    public ScreeningReport ScreeningReport { get; set; } = null!;

    public ApplicationUser Learner { get; set; } = null!;

    public ApplicationUser? AssignedPsychologist { get; set; }
}
