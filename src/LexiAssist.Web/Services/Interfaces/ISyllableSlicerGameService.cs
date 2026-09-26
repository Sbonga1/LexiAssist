using LexiAssist.Web.Models.Activities;

namespace LexiAssist.Web.Services.Interfaces;

public interface ISyllableSlicerGameService
{
    Task<SyllableSlicerSession?> GetSessionAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<SyllableSlicerSession?> StartAsync(
        string learnerId,
        int assignmentId,
        CancellationToken cancellationToken = default);

    Task<SyllableSlicerSession?> SubmitAsync(
        string learnerId,
        int assignmentId,
        int attemptId,
        string itemId,
        string orderedSegmentIndexes,
        Guid submissionId,
        CancellationToken cancellationToken = default);
}

public sealed record SyllableSlicerChunk(int Index, string Text);

public sealed record SyllableSlicerQuestion(
    string ItemId,
    string TargetWord,
    string Prompt,
    IReadOnlyList<SyllableSlicerChunk> Chunks);

public sealed record SyllableSlicerSession(
    int AssignmentId,
    bool CanStart,
    bool IsCompleted,
    int? AttemptId,
    int ItemsCompleted,
    int TotalItems,
    int AttemptsCount,
    int CorrectCount,
    double? Accuracy,
    long? DurationMilliseconds,
    SyllableSlicerQuestion? CurrentQuestion,
    string OrderedSegmentIndexes,
    string? Feedback,
    string? Message);

public sealed class SyllableSlicerAnswerException(string message) : InvalidOperationException(message);
