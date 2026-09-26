namespace LexiAssist.Web.Models.Activities;

public sealed class SoundBuilderOptions
{
    public string ContentVersion { get; set; } = "1.0";

    public List<SoundBuilderContentItem> Items { get; set; } = [];
}

public sealed class SoundBuilderContentItem
{
    public string Id { get; set; } = string.Empty;

    public SoundBuilderMode Mode { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public string Word { get; set; } = string.Empty;

    public int DifficultyLevel { get; set; } = 3;

    public List<string> Chunks { get; set; } = [];
}
