using LexiAssist.Web.Models.Referrals;

namespace LexiAssist.Web.ViewModels;

public sealed record PsychologistDashboardViewModel(
    IReadOnlyList<PsychologistDashboardReferralViewModel> AssignedPendingReferrals,
    IReadOnlyList<PsychologistDashboardReferralViewModel> ReferralsUnderReview,
    IReadOnlyList<PsychologistDashboardReferralViewModel> RecentlyReviewedCases);

public sealed record PsychologistDashboardReferralViewModel(
    int ReferralId,
    string LearnerName,
    string? RiskTier,
    DateTime ReferralDateUtc,
    DateTime? ReviewedAtUtc,
    ReferralStatus Status);
