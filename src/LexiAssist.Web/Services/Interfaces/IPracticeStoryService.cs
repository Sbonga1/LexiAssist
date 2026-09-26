using LexiAssist.Web.Models.Activities;

namespace LexiAssist.Web.Services.Interfaces;

public interface IPracticeStoryService
{
    PracticeStoryDraft CreateDraft(
        string targetPhoneticPattern,
        IEnumerable<string> targetWords,
        string learnerSelectedInterest,
        int difficultyLevel);

    Task<PracticeStory> GenerateAsync(
        string learnerId,
        string targetPhoneticPattern,
        IEnumerable<string> targetWords,
        string learnerSelectedInterest,
        int difficultyLevel,
        CancellationToken cancellationToken = default);
}

public sealed record PracticeStoryDraft(
    string Theme,
    string TargetPhoneticPattern,
    IReadOnlyList<string> TargetWords,
    int DifficultyLevel,
    string StoryText,
    string GenerationMethod);
