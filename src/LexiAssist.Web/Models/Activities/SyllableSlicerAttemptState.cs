namespace LexiAssist.Web.Models.Activities;

/// <summary>Configured word snapshots and learner responses for one Syllable Slicer activity attempt.</summary>
public sealed class SyllableSlicerAttemptState
{
    public string ContentVersion { get; set; } = string.Empty;

    public AdaptiveSupportState AdaptiveSupport { get; set; } = new();

    public int CurrentItemIndex { get; set; }

    public DateTime CurrentItemStartedAtUtc { get; set; }

    public List<SyllableSlicerAttemptItem> Items { get; set; } = [];

    public List<SyllableSlicerAttemptResponse> Responses { get; set; } = [];
}

public sealed class SyllableSlicerAttemptItem
{
    public string Id { get; set; } = string.Empty;

    public string TargetWord { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public int DifficultyLevel { get; set; } = 3;

    public List<string> Segments { get; set; } = [];

    /// <summary>Original segment indexes in the randomized order shown to the learner.</summary>
    public List<int> PresentedSegmentIndexes { get; set; } = [];
}

public sealed class SyllableSlicerAttemptResponse
{
    public Guid SubmissionId { get; set; }

    public string ItemId { get; set; } = string.Empty;

    public string TargetWord { get; set; } = string.Empty;

    public List<int> OrderedSegmentIndexes { get; set; } = [];

    public bool IsCorrect { get; set; }

    public int DifficultyLevel { get; set; } = 3;

    public long ResponseTimeMilliseconds { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
