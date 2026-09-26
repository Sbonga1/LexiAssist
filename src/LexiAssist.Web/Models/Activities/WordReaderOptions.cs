namespace LexiAssist.Web.Models.Activities;

public sealed class WordReaderOptions
{
    public string ContentVersion { get; set; } = "1.0";

    public List<WordReaderSkillSet> SkillSets { get; set; } = [];
}

public sealed class WordReaderSkillSet
{
    public string SkillArea { get; set; } = string.Empty;

    public List<WordReaderContentItem> Items { get; set; } = [];
}

public sealed class WordReaderContentItem
{
    public string Id { get; set; } = string.Empty;

    public string Word { get; set; } = string.Empty;

    public int DifficultyLevel { get; set; } = 3;

    /// <summary>Optional local static path under wwwroot/media/word-reader.</summary>
    public string? AudioPath { get; set; }
}
