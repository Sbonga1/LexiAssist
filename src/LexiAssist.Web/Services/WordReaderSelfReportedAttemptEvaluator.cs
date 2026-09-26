using LexiAssist.Web.Services.Interfaces;

namespace LexiAssist.Web.Services;

/// <summary>Stores self-reported practice without interpreting pronunciation or claiming correctness.</summary>
public sealed class WordReaderSelfReportedAttemptEvaluator : IWordReaderAttemptEvaluator
{
    public Task<WordReaderAttemptEvaluation> EvaluateAsync(
        WordReaderAttemptEvaluationContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WordReaderAttemptEvaluation(IsCorrect: null, Method: "SelfReportedOnly"));
}
