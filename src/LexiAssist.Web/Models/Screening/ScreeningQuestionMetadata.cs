namespace LexiAssist.Web.Models.Screening;

public sealed class ScreeningQuestionMetadata
{
    public List<string>? ChoiceOptions { get; set; }

    public string? PassageText { get; set; }

    public int? ReferenceWordCount { get; set; }

    public int? TargetWordsPerMinute { get; set; }

    public int? TimeLimitSeconds { get; set; }

    public int? MaxAudioReplays { get; set; }

    public string? AudioSource { get; set; }

    public int? AudioDurationSeconds { get; set; }

    public string? Transcript { get; set; }

    public string? ResponseType { get; set; }

    public List<string>? TimedNamingItems { get; set; }

    public List<string>? WorkingMemorySequence { get; set; }

    public int? MemoryDisplayDurationMilliseconds { get; set; }

    public Dictionary<string, string>? TimedMatchingReferenceMapping { get; set; }

    public List<string>? TimedMatchingItems { get; set; }

    public int? PatternDifficultyLevel { get; set; }
}
