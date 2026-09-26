using System.ComponentModel.DataAnnotations;

namespace LexiAssist.Web.Models.Notifications;

public enum AppNotificationType
{
    [Display(Name = "Screening completed")]
    ScreeningCompleted = 1,

    [Display(Name = "Referral created")]
    ReferralCreated = 2,

    [Display(Name = "Referral assigned")]
    ReferralAssigned = 3,

    [Display(Name = "Professional follow-up requested")]
    ProfessionalFollowUpRequested = 4,

    [Display(Name = "Learning activity assigned")]
    LearningActivityAssigned = 5
}
