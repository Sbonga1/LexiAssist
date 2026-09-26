using LexiAssist.Web.Models.Activities;

namespace LexiAssist.Web.Services.Interfaces;

public interface IFastNameRaceGameService
{
    Task<FastNameRaceSession?> GetSessionAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<FastNameRaceSession?> StartAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<FastNameRaceSession?> CompleteAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        int itemsCompleted,
        string? errorIndexesJson,
        long activeDurationMilliseconds,
        CancellationToken cancellationToken = default);
}

public sealed record FastNameRaceSession(
    int AssignmentId,
    string ActivityName,
    bool CanStart,
    bool IsCompleted,
    int? AttemptId,
    int DurationSeconds,
    IReadOnlyList<string> Items,
    int ItemsCompleted,
    int ErrorCount,
    long? ActiveDurationMilliseconds,
    double? ItemsPerMinute,
    double? Accuracy,
    string? Message);

public sealed class FastNameRaceSubmissionException(string message) : InvalidOperationException(message);
