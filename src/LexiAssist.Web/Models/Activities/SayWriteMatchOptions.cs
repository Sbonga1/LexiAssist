namespace LexiAssist.Web.Models.Activities;

public sealed class SayWriteMatchOptions
{
    public string ContentVersion { get; set; } = "1.0";

    public List<SayWriteMatchItem> Items { get; set; } = [];
}

public sealed class SayWriteMatchItem
{
    public string Id { get; set; } = string.Empty;

    public string Word { get; set; } = string.Empty;

    /// <summary>A short clue shown whether or not optional pronunciation audio is available.</summary>
    public string Prompt { get; set; } = string.Empty;

    public int DifficultyLevel { get; set; } = 3;

    /// <summary>Optional local pronunciation audio path below wwwroot/media/say-write-match.</summary>
    public string? AudioPath { get; set; }

    /// <summary>Ordered letter or spelling chunks which, when joined, form <see cref="Word"/>.</summary>
    public List<string> Chunks { get; set; } = [];
}
