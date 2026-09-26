namespace LexiAssist.Web.Models.Activities;

public sealed class FastNameRaceOptions
{
    public string ContentVersion { get; set; } = "1.0";

    public int DurationSeconds { get; set; } = 60;

    /// <summary>
    /// Configured grid labels. Content may mix symbols, letters, numbers, and familiar words.
    /// </summary>
    public List<string> Items { get; set; } = [];
}
