using LexiAssist.Web.Models.Activities;

namespace LexiAssist.Web.Services.Interfaces;

public interface ISayWriteMatchGameService
{
    Task<SayWriteMatchSession?> GetSessionAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<SayWriteMatchSession?> StartAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<SayWriteMatchSession?> SubmitAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        string itemId,
        string orderedChunkIndexes,
        Guid submissionId,
        CancellationToken cancellationToken = default);
}

public sealed record SayWriteMatchChunk(int Index, string Text);

public sealed record SayWriteMatchQuestion(
    string ItemId,
    string Prompt,
    string? AudioPath,
    bool AudioUnavailable,
    IReadOnlyList<SayWriteMatchChunk> Chunks);

public sealed record SayWriteMatchSession(
    int AssignmentId,
    bool CanStart,
    bool IsCompleted,
    int? AttemptId,
    int ItemsCompleted,
    int TotalItems,
    int ResponsesCount,
    int CorrectCount,
    double? Accuracy,
    long? DurationMilliseconds,
    SayWriteMatchQuestion? CurrentQuestion,
    string OrderedChunkIndexes,
    string? Feedback,
    string? Message);

public sealed class SayWriteMatchAnswerException(string message) : InvalidOperationException(message);
