namespace LexiAssist.Web.Models.Activities;

public class ActivityAttempt
{
    public int Id { get; set; }

    public int ActivityAssignmentId { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public double? Score { get; set; }

    public double? Accuracy { get; set; }

    public long? DurationMilliseconds { get; set; }

    public string? MetadataJson { get; set; }

    public ActivityAssignment ActivityAssignment { get; set; } = null!;
}
