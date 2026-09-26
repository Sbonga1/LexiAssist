namespace LexiAssist.Web.Models.Screening;

public class ScreeningTestAttempt
{
    public int Id { get; set; }

    public int ScreeningAttemptId { get; set; }

    public int ScreeningTestId { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public double? RawScore { get; set; }

    public double? PercentageScore { get; set; }

    public bool? HasFlaggedSkillArea { get; set; }

    public long? DurationMilliseconds { get; set; }

    public ScreeningAttempt ScreeningAttempt { get; set; } = null!;

    public ScreeningTest ScreeningTest { get; set; } = null!;

    public ICollection<ScreeningResponse> Responses { get; set; } = new List<ScreeningResponse>();
}
