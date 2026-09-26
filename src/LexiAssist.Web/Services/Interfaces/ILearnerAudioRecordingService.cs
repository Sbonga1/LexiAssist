using LexiAssist.Web.Models.Recordings;

namespace LexiAssist.Web.Services.Interfaces;

public interface ILearnerAudioRecordingService
{
    Task<LearnerAudioRecording?> StartForScreeningAsync(
        string userId,
        int screeningTestAttemptId,
        int screeningQuestionId,
        bool consentAccepted,
        CancellationToken cancellationToken = default);

    Task<LearnerAudioRecording?> StartForActivityAsync(
        string userId,
        int activityAttemptId,
        bool consentAccepted,
        CancellationToken cancellationToken = default);

    Task<LearnerAudioRecordingUploadResult> CompleteAsync(
        string userId,
        int recordingId,
        Stream content,
        long fileSizeBytes,
        string? contentType,
        long durationMilliseconds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LearnerAudioRecording>> GetScreeningRecordingsAsync(
        string userId,
        int screeningTestAttemptId,
        int screeningQuestionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LearnerAudioRecording>> GetActivityRecordingsAsync(
        string userId,
        int activityAttemptId,
        CancellationToken cancellationToken = default);

    Task<LearnerAudioRecordingFile?> GetAuthorizedFileAsync(
        string userId,
        bool isAdministrator,
        bool isPsychologist,
        int recordingId,
        CancellationToken cancellationToken = default);
}

public sealed record LearnerAudioRecordingUploadResult(
    LearnerAudioRecording? Recording,
    string? Error)
{
    public bool Succeeded => Recording is not null && Error is null;
}

public sealed record LearnerAudioRecordingFile(
    Stream Content,
    string ContentType,
    long Length);
