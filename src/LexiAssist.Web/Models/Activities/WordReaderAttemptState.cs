namespace LexiAssist.Web.Models.Activities;

/// <summary>Configured words and self-reported practice responses captured for one attempt.</summary>
public sealed class WordReaderAttemptState
{
    public string ContentVersion { get; set; } = string.Empty;

    public string SkillArea { get; set; } = string.Empty;

    public AdaptiveSupportState AdaptiveSupport { get; set; } = new();

    public int CurrentItemIndex { get; set; }

    public List<WordReaderAttemptItem> Items { get; set; } = [];

    public List<WordReaderAttemptResponse> Responses { get; set; } = [];
}

public sealed class WordReaderAttemptItem
{
    public string Id { get; set; } = string.Empty;

    public string Word { get; set; } = string.Empty;

    public int DifficultyLevel { get; set; } = 3;

    public string? AudioPath { get; set; }

    public bool AudioUnavailable { get; set; }
}

public sealed class WordReaderAttemptResponse
{
    public Guid SubmissionId { get; set; }

    public string ItemId { get; set; } = string.Empty;

    public WordReaderPracticeOutcome Outcome { get; set; }

    public bool? IsCorrect { get; set; }

    public int DifficultyLevel { get; set; } = 3;

    public string EvaluationMethod { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}

public enum WordReaderPracticeOutcome
{
    ReadWithoutHelp = 1,
    PractisedWithSupport = 2,
    RetryRequested = 3
}
