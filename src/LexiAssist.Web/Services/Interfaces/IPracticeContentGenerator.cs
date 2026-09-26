namespace LexiAssist.Web.Services.Interfaces;

/// <summary>
/// Provider-neutral practice-content generation boundary. Implementations may
/// use templates or a future external provider, but generated content remains
/// learning content and is never used for scoring or clinical decisions.
/// </summary>
public interface IPracticeContentGenerator
{
    PracticeContentGenerationResult Generate(PracticeContentGenerationRequest request);

    Task<PracticeContentGenerationResult> GenerateAsync(
        PracticeContentGenerationRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Generate(request));
}

public sealed record PracticeContentGenerationRequest(
    string TargetPhoneticPattern,
    IReadOnlyList<string> TargetWords,
    string LearnerSelectedInterest,
    int DifficultyLevel);

public sealed record PracticeContentGenerationResult(
    string Theme,
    string TargetPhoneticPattern,
    IReadOnlyList<string> TargetWords,
    int DifficultyLevel,
    string StoryText,
    string GenerationMethod);
