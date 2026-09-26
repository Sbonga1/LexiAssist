using LexiAssist.Web.Models.Activities;

namespace LexiAssist.Web.Services.Interfaces;

public interface IWordReaderAttemptEvaluator
{
    Task<WordReaderAttemptEvaluation> EvaluateAsync(
        WordReaderAttemptEvaluationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Input boundary for a future evaluator. AudioRecordingPath remains null until recording is implemented.
/// </summary>
public sealed record WordReaderAttemptEvaluationContext(
    string TargetWord,
    WordReaderPracticeOutcome SelfReportedOutcome,
    string? AudioRecordingPath);

public sealed record WordReaderAttemptEvaluation(bool? IsCorrect, string Method);
