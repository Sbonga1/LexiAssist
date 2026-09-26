namespace LexiAssist.Web.Models.Activities;

public sealed class SyllableSlicerOptions
{
    public string ContentVersion { get; set; } = "1.0";

    public List<SyllableSlicerWord> Items { get; set; } = [];
}

public sealed class SyllableSlicerWord
{
    public string Id { get; set; } = string.Empty;

    public string Word { get; set; } = string.Empty;

    public string Prompt { get; set; } = "Arrange the syllable chunks to build the word.";

    public int DifficultyLevel { get; set; } = 3;

    /// <summary>Ordered syllable segments which, when joined, form <see cref="Word"/>.</summary>
    public List<string> Segments { get; set; } = [];
}
