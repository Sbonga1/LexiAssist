using Microsoft.AspNetCore.Identity;
using LexiAssist.Web.Models.Referrals;
using LexiAssist.Web.Models.Screening;
using LexiAssist.Web.Models.Notifications;

namespace LexiAssist.Web.Models.Identity;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public DateOnly? DateOfBirth { get; set; }

    public string? PreferredName { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ScreeningPrivacyNoticeAcceptedAtUtc { get; set; }

    public ICollection<ScreeningAttempt> ScreeningAttempts { get; set; } = new List<ScreeningAttempt>();

    public ICollection<Referral> ReferralsAsLearner { get; set; } = new List<Referral>();

    public ICollection<Referral> AssignedReferrals { get; set; } = new List<Referral>();

    public ICollection<AppNotification> Notifications { get; set; } = new List<AppNotification>();

    public ICollection<LearnerInterest> LearnerInterests { get; set; } = new List<LearnerInterest>();

    public LearnerReminderPreference? LearnerReminderPreference { get; set; }

    public ICollection<Activities.PracticeStory> PracticeStories { get; set; } = new List<Activities.PracticeStory>();
}
