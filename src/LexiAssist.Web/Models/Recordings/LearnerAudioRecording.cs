using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Models.Screening;

namespace LexiAssist.Web.Models.Recordings;

public enum LearnerAudioRecordingStatus
{
    PendingUpload = 0,
    Available = 1,
    Expired = 2
}

public sealed class LearnerAudioRecording
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public int? ScreeningTestAttemptId { get; set; }

    public int? ScreeningQuestionId { get; set; }

    public int? ActivityAttemptId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ConsentAcceptedAtUtc { get; set; }

    public string ConsentNoticeVersion { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }

    public DateTime? UploadedAtUtc { get; set; }

    public long? DurationMilliseconds { get; set; }

    public long? FileSizeBytes { get; set; }

    public string? ContentType { get; set; }

    public string? StorageFileName { get; set; }

    public string? ProfessionalNote { get; set; }

    public DateTime? ProfessionalNoteUpdatedAtUtc { get; set; }

    public LearnerAudioRecordingStatus Status { get; set; } = LearnerAudioRecordingStatus.PendingUpload;

    public ScreeningTestAttempt? ScreeningTestAttempt { get; set; }

    public ScreeningQuestion? ScreeningQuestion { get; set; }

    public ActivityAttempt? ActivityAttempt { get; set; }
}
