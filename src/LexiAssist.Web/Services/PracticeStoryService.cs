using System.Text.Json;
using LexiAssist.Web.Data;
using LexiAssist.Web.Models.Activities;
using LexiAssist.Web.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace LexiAssist.Web.Services;

/// <summary>
/// Compatibility facade for the original story-specific API. New callers
/// should depend on IPracticeContentGenerator so another provider can be added
/// without changing the story persistence service.
/// </summary>
public sealed class PracticeStoryTemplateGenerator
{
    public const string GenerationMethod = TemplatePracticeContentGenerator.GenerationMethod;

    private readonly TemplatePracticeContentGenerator generator = new();

    public PracticeStoryDraft Generate(
        string targetPhoneticPattern,
        IEnumerable<string> targetWords,
        string learnerSelectedInterest,
        int difficultyLevel)
    {
        var result = generator.Generate(new PracticeContentGenerationRequest(
            targetPhoneticPattern,
            targetWords.ToList(),
            learnerSelectedInterest,
            difficultyLevel));
        return new PracticeStoryDraft(
            result.Theme,
            result.TargetPhoneticPattern,
            result.TargetWords,
            result.DifficultyLevel,
            result.StoryText,
            result.GenerationMethod);
    }
}

public sealed class PracticeStoryService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IPracticeContentGenerator contentGenerator) : IPracticeStoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PracticeStoryDraft CreateDraft(
        string targetPhoneticPattern,
        IEnumerable<string> targetWords,
        string learnerSelectedInterest,
        int difficultyLevel)
    {
        var result = contentGenerator.Generate(new PracticeContentGenerationRequest(
            targetPhoneticPattern,
            targetWords.ToList(),
            learnerSelectedInterest,
            difficultyLevel));
        return new PracticeStoryDraft(
            result.Theme,
            result.TargetPhoneticPattern,
            result.TargetWords,
            result.DifficultyLevel,
            result.StoryText,
            result.GenerationMethod);
    }

    public async Task<PracticeStory> GenerateAsync(
        string learnerId,
        string targetPhoneticPattern,
        IEnumerable<string> targetWords,
        string learnerSelectedInterest,
        int difficultyLevel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(learnerId))
        {
            throw new ArgumentException("A learner identifier is required.", nameof(learnerId));
        }

        var draftResult = await contentGenerator.GenerateAsync(
            new PracticeContentGenerationRequest(
                targetPhoneticPattern,
                targetWords.ToList(),
                learnerSelectedInterest,
                difficultyLevel),
            cancellationToken);
        var story = new PracticeStory
        {
            LearnerId = learnerId.Trim(),
            Theme = draftResult.Theme,
            TargetPhoneticPattern = draftResult.TargetPhoneticPattern,
            TargetWordsJson = JsonSerializer.Serialize(draftResult.TargetWords, JsonOptions),
            DifficultyLevel = draftResult.DifficultyLevel,
            StoryText = draftResult.StoryText,
            GenerationMethod = draftResult.GenerationMethod,
            GeneratedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        };

        dbContext.PracticeStories.Add(story);
        await dbContext.SaveChangesAsync(cancellationToken);
        return story;
    }
}
