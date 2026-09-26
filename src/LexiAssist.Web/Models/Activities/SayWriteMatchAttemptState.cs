namespace LexiAssist.Web.Models.Activities;

/// <summary>Configured spelling items and learner responses saved for one activity attempt.</summary>
public sealed class SayWriteMatchAttemptState
{
    public string ContentVersion { get; set; } = string.Empty;

    public AdaptiveSupportState AdaptiveSupport { get; set; } = new();

    public int CurrentItemIndex { get; set; }

    public DateTime CurrentItemStartedAtUtc { get; set; }

    public List<SayWriteMatchAttemptItem> Items { get; set; } = [];

    public List<SayWriteMatchAttemptResponse> Responses { get; set; } = [];
}

public sealed class SayWriteMatchAttemptItem
{
    public string Id { get; set; } = string.Empty;

    public string Word { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public int DifficultyLevel { get; set; } = 3;

    public string? AudioPath { get; set; }

    public bool AudioUnavailable { get; set; }

    public List<string> Chunks { get; set; } = [];

    /// <summary>Original chunk indexes in the scrambled order initially shown to the learner.</summary>
    public List<int> ScrambledChunkIndexes { get; set; } = [];
}

public sealed class SayWriteMatchAttemptResponse
{
    public Guid SubmissionId { get; set; }

    public string ItemId { get; set; } = string.Empty;

    public string TargetWord { get; set; } = string.Empty;

    public List<int> OrderedChunkIndexes { get; set; } = [];

    public bool IsCorrect { get; set; }

    public int DifficultyLevel { get; set; } = 3;

    public long ResponseTimeMilliseconds { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
