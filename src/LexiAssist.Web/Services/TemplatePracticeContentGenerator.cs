using LexiAssist.Web.Services.Interfaces;

namespace LexiAssist.Web.Services;

/// <summary>
/// The default local generator. It has no network or provider dependency and
/// produces the same result for the same request.
/// </summary>
public sealed class TemplatePracticeContentGenerator : IPracticeContentGenerator
{
    public const string GenerationMethod = "deterministic-template-v1";

    private static readonly IReadOnlyDictionary<string, string> ThemeOpenings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["space"] = "On a quiet space mission, a curious learner spots",
            ["football"] = "At a friendly football practice, a learner notices",
            ["dinosaurs"] = "At the dinosaur museum, a curious learner discovers",
            ["technology"] = "In a small technology workshop, a learner tests",
            ["animals"] = "At the animal shelter, a learner helps",
            ["music"] = "Before music practice, a learner listens for"
        };

    private static readonly IReadOnlyDictionary<string, string> ThemeActivities =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["space"] = "The learner records each word in a mission log and checks the sound pattern carefully.",
            ["football"] = "The learner writes each word on a team card and checks the sound pattern carefully.",
            ["dinosaurs"] = "The learner adds each word to a fossil notebook and checks the sound pattern carefully.",
            ["technology"] = "The learner saves each word in a project note and checks the sound pattern carefully.",
            ["animals"] = "The learner writes each word on an animal care card and checks the sound pattern carefully.",
            ["music"] = "The learner writes each word on a music page and checks the sound pattern carefully."
        };

    public PracticeContentGenerationResult Generate(PracticeContentGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pattern = NormalizeRequiredText(request.TargetPhoneticPattern, "target phonetic pattern", 120);
        var words = NormalizeWords(request.TargetWords);
        var interest = NormalizeRequiredText(request.LearnerSelectedInterest, "learner interest", 80);
        if (request.DifficultyLevel is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(request.DifficultyLevel), "Difficulty level must be between 1 and 5.");
        }

        var opening = ThemeOpenings.TryGetValue(interest, out var knownOpening)
            ? knownOpening
            : $"While exploring {interest}, a curious learner finds";
        var activity = ThemeActivities.TryGetValue(interest, out var knownActivity)
            ? knownActivity
            : "The learner keeps the words in a small practice note and checks the sound pattern carefully.";
        var wordList = JoinWords(words);
        var difficultySentence = request.DifficultyLevel <= 2
            ? "The learner takes one word at a time and pauses whenever needed."
            : request.DifficultyLevel >= 4
                ? "The learner tries each word in a new sentence and pauses whenever needed."
                : "The learner practises each word in order and pauses whenever needed.";

        return new PracticeContentGenerationResult(
            interest,
            pattern,
            words,
            request.DifficultyLevel,
            $"{opening} {wordList}. {activity} {difficultySentence}",
            GenerationMethod);
    }

    private static IReadOnlyList<string> NormalizeWords(IEnumerable<string> targetWords)
    {
        ArgumentNullException.ThrowIfNull(targetWords);

        var words = targetWords
            .Select(word => NormalizeWord(word))
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (words.Count is < 1 or > 12)
        {
            throw new ArgumentException("Provide between one and twelve target words.", nameof(targetWords));
        }

        return words;
    }

    private static string NormalizeWord(string? word)
    {
        var normalized = NormalizeRequiredText(word, "target word", 80)
            .Trim('.', ',', ';', ':', '!', '?');
        if (normalized.Length == 0 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Target words must contain readable text.", nameof(word));
        }

        return normalized;
    }

    private static string NormalizeRequiredText(string? value, string fieldName, int maxLength)
    {
        var normalized = string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"{fieldName} is required.", fieldName);
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{fieldName} is too long.", fieldName);
        }

        if (normalized.Any(character => character is '.' or '!' or '?'))
        {
            throw new ArgumentException($"{fieldName} cannot contain sentence punctuation.", fieldName);
        }

        return normalized;
    }

    private static string JoinWords(IReadOnlyList<string> words) => words.Count switch
    {
        1 => $"the word {words[0]}",
        2 => $"the words {words[0]} and {words[1]}",
        _ => $"the words {string.Join(", ", words.Take(words.Count - 1))}, and {words[^1]}"
    };
}
