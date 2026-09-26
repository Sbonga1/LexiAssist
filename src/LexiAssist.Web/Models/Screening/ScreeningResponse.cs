namespace LexiAssist.Web.Models.Screening;

public class ScreeningResponse
{
    public int Id { get; set; }

    public int ScreeningTestAttemptId { get; set; }

    public int ScreeningQuestionId { get; set; }

    public string? TextAnswer { get; set; }

    public string? SelectedAnswer { get; set; }

    public bool? IsCorrect { get; set; }

    public double? Score { get; set; }

    public long? ResponseTimeMilliseconds { get; set; }

    public string? AudioRecordingPath { get; set; }

    public string? ProfessionalAudioNote { get; set; }

    public DateTime? ProfessionalAudioNoteUpdatedAtUtc { get; set; }

    public string? ResponseDetailsJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ScreeningTestAttempt ScreeningTestAttempt { get; set; } = null!;

    public ScreeningQuestion ScreeningQuestion { get; set; } = null!;

    public ICollection<ScreeningResponseErrorCategory> ErrorCategories { get; set; } = new List<ScreeningResponseErrorCategory>();
}
