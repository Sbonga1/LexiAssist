using LexiAssist.Web.Models.Identity;
using LexiAssist.Web.Models.Reports;

namespace LexiAssist.Web.Models.Activities;

public class ActivityAssignment
{
    public int Id { get; set; }

    public string LearnerId { get; set; } = string.Empty;

    public int LearningActivityId { get; set; }

    public string? AssignedByUserId { get; set; }

    public int? RelatedScreeningReportId { get; set; }

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public ActivityAssignmentStatus Status { get; set; } = ActivityAssignmentStatus.Assigned;

    public ApplicationUser Learner { get; set; } = null!;

    public ApplicationUser? AssignedByUser { get; set; }

    public LearningActivity LearningActivity { get; set; } = null!;

    public ScreeningReport? RelatedScreeningReport { get; set; }

    public ICollection<ActivityAttempt> Attempts { get; set; } = new List<ActivityAttempt>();
}
