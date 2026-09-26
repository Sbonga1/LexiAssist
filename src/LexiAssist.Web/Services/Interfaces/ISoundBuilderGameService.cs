using LexiAssist.Web.Models.Activities;

namespace LexiAssist.Web.Services.Interfaces;

public interface ISoundBuilderGameService
{
    Task<SoundBuilderSession?> GetSessionAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<SoundBuilderSession?> StartAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<SoundBuilderSession?> SubmitAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        string itemId,
        string orderedChunkIndexes,
        string? blendedWord,
        string? segmentedChunks,
        CancellationToken cancellationToken = default);
}

public sealed record SoundBuilderChunk(int Index, string Text);

public sealed record SoundBuilderQuestion(
    string ItemId,
    SoundBuilderMode Mode,
    string Prompt,
    string Word,
    IReadOnlyList<SoundBuilderChunk> Chunks);

public sealed record SoundBuilderSession(
    int AssignmentId,
    string ActivityName,
    bool CanStart,
    bool IsCompleted,
    int? AttemptId,
    int CompletedItems,
    int TotalItems,
    int CorrectItems,
    double? Accuracy,
    long? DurationMilliseconds,
    string? Feedback,
    string? Message,
    SoundBuilderQuestion? CurrentQuestion);

public sealed class SoundBuilderAnswerException(string message) : InvalidOperationException(message);
