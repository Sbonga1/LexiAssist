using LexiAssist.Web.Models.Activities;

namespace LexiAssist.Web.Services.Interfaces;

public interface IWordReaderGameService
{
    Task<WordReaderSession?> GetSessionAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<WordReaderSession?> StartAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<WordReaderSession?> RespondAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        string itemId,
        Guid submissionId,
        WordReaderPracticeOutcome outcome,
        CancellationToken cancellationToken = default);
}

public sealed record WordReaderPracticeItem(string Id, string Word, string? AudioPath, bool AudioUnavailable);

public sealed record WordReaderSession(
    int AssignmentId,
    string ActivityName,
    string? SkillArea,
    bool CanStart,
    bool IsCompleted,
    int? AttemptId,
    int ItemsCompleted,
    int TotalItems,
    int ResponseCount,
    int RetryCount,
    int SupportedPracticeCount,
    long? DurationMilliseconds,
    WordReaderPracticeItem? CurrentItem,
    string? Feedback,
    string? Message);

public sealed class WordReaderAnswerException(string message) : InvalidOperationException(message);
