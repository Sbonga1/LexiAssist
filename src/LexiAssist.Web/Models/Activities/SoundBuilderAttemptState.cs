namespace LexiAssist.Web.Models.Activities;

/// <summary>
/// Saved content and responses for one Sound Builder play-through.
/// Keeping a content snapshot makes the attempt understandable if appsettings changes later.
/// </summary>
public sealed class SoundBuilderAttemptState
{
    public string ContentVersion { get; set; } = string.Empty;

    public AdaptiveSupportState AdaptiveSupport { get; set; } = new();

    public List<SoundBuilderAttemptItem> Items { get; set; } = [];

    public List<SoundBuilderItemResponse> Responses { get; set; } = [];
}

public sealed class SoundBuilderAttemptItem
{
    public string Id { get; set; } = string.Empty;

    public SoundBuilderMode Mode { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public string Word { get; set; } = string.Empty;

    public int DifficultyLevel { get; set; } = 3;

    public List<string> Chunks { get; set; } = [];

    public List<int> PresentationOrder { get; set; } = [];
}

public sealed class SoundBuilderItemResponse
{
    public string ItemId { get; set; } = string.Empty;

    public List<int> OrderedChunkIndexes { get; set; } = [];

    public List<string> OrderedChunks { get; set; } = [];

    public string? BlendedWord { get; set; }

    public List<string> SegmentedChunks { get; set; } = [];

    public bool IsCorrect { get; set; }

    public int DifficultyLevel { get; set; } = 3;

    public DateTime SubmittedAtUtc { get; set; }
}
